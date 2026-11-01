using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PeerCare.Api;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

var builder = WebApplication.CreateBuilder(args);
var keyRingPath = builder.Configuration["Security:DataProtectionKeysPath"];
if (string.IsNullOrWhiteSpace(keyRingPath) && builder.Environment.IsDevelopment())
    keyRingPath = Path.Combine(builder.Environment.ContentRootPath, ".local-data-protection-keys");
if (string.IsNullOrWhiteSpace(keyRingPath) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Security:DataProtectionKeysPath must point to a persistent, protected key ring outside development.");
Directory.CreateDirectory(keyRingPath!);
builder.Services.AddDataProtection().SetApplicationName("PeerCare").PersistKeysToFileSystem(new DirectoryInfo(keyRingPath!));
builder.Services.AddSingleton<ContactCrypto>();
builder.Services.AddSingleton<ILookupNormalizer, HmacLookupNormalizer>();
var databaseProvider = builder.Configuration["Database:Provider"] ?? "PostgreSQL";
builder.Services.AddDbContext<PeerCareDbContext>(options =>
{
    if (databaseProvider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
    {
        var databaseFile = builder.Configuration["Database:SqliteFile"] ?? "peercare.local.db";
        options.UseSqlite($"Data Source={Path.Combine(builder.Environment.ContentRootPath, databaseFile)}");
    }
    else if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(builder.Configuration.GetConnectionString("PeerCare"), npgsql => npgsql.EnableRetryOnFailure(3));
    else throw new InvalidOperationException("Database:Provider must be SQLite or PostgreSQL.");
});
builder.Services.AddIdentity<PeerUser, PeerRole>(options =>
    {
        // Email accounts are checked by Identity and the database's unique index;
        // phone-only accounts have no email and must still be allowed to register.
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedPhoneNumber = false;
        options.Lockout.MaxFailedAccessAttempts = 3;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.Password.RequiredLength = 14;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddEntityFrameworkStores<PeerCareDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "peercare.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-XSRF-TOKEN"; options.Cookie.Name = "peercare.csrf"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Lax; });
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("termii", client => client.Timeout = TimeSpan.FromSeconds(12));
builder.Services.AddHttpClient("mailjet", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<ContactDelivery>();
builder.Services.AddScoped<PeerAuditService>();
builder.Services.AddScoped<PeerNotificationService>();
builder.Services.AddHostedService<AccountPurgeService>();
builder.Services.AddCors(options => options.AddPolicy("client", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("ClientOrigins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyMethod().AllowAnyHeader().AllowCredentials()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("otp", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("search", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("posting", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Moderator", p => p.RequireRole("Moderator", "Admin"));
    options.AddPolicy("Admin", p => p.RequireRole("Admin"));
    options.AddPolicy("Professional", p => p.RequireRole("Professional", "Moderator", "Admin"));
});

var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.XFrameOptions = "DENY";
    await next();
});
app.UseCors("client");
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var isUnsafeMethod = HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method);
    if (isUnsafeMethod && context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/api/auth/code"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException ex)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Antiforgery")
                .LogWarning("CSRF validation rejected {Method} {Path}: {Reason}. Authenticated={Authenticated}, AuthCookiePresent={AuthCookiePresent}, CsrfCookiePresent={CsrfCookiePresent}, HeaderPresent={HeaderPresent}",
                    context.Request.Method, context.Request.Path, ex.Message, context.User.Identity?.IsAuthenticated == true,
                    context.Request.Cookies.ContainsKey("peercare.auth"), context.Request.Cookies.ContainsKey("peercare.csrf"),
                    context.Request.Headers.ContainsKey("X-XSRF-TOKEN"));
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "Refresh the page and try again." });
            return;
        }
    }
    await next();
});
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Migration"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<PeerCareDbContext>();
    if (db.Database.IsSqlite())
    {
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS health_entries (
                Id TEXT NOT NULL CONSTRAINT PK_health_entries PRIMARY KEY,
                UserId TEXT NOT NULL,
                EntryType TEXT NOT NULL,
                Title TEXT NULL,
                Description TEXT NOT NULL,
                OccurredAt TEXT NULL,
                Severity INTEGER NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                CONSTRAINT FK_health_entries_users_UserId FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_health_entries_UserId_OccurredAt ON health_entries (UserId, OccurredAt);
            """);
        await db.Database.OpenConnectionAsync();
        foreach (var (table, column, definition) in new[]
        {
            ("posts", "DiagnosisStatus", "TEXT NOT NULL DEFAULT 'not_stated'"),
            ("facilities", "Source", "TEXT NULL"),
            ("facilities", "LastVerifiedAt", "TEXT NULL"),
            ("users", "Bio", "TEXT NULL"),
            ("users", "ProfileVisibility", "TEXT NOT NULL DEFAULT 'public'")
        })
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
            try { await command.ExecuteNonQueryAsync(); }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)) { }
        }
        await db.Database.CloseConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS communities (
                Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Slug TEXT NOT NULL, Description TEXT NOT NULL,
                Guidelines TEXT NOT NULL, Status TEXT NOT NULL, ConditionId TEXT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ConditionId) REFERENCES conditions (Id) ON DELETE SET NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_communities_Slug ON communities (Slug);
            CREATE INDEX IF NOT EXISTS IX_communities_Status_Name ON communities (Status, Name);
            CREATE INDEX IF NOT EXISTS IX_communities_ConditionId ON communities (ConditionId);
            CREATE TABLE IF NOT EXISTS community_memberships (
                CommunityId TEXT NOT NULL, UserId TEXT NOT NULL, Status TEXT NOT NULL, JoinedAt TEXT NOT NULL,
                PRIMARY KEY (CommunityId, UserId),
                FOREIGN KEY (CommunityId) REFERENCES communities (Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_community_memberships_UserId_Status ON community_memberships (UserId, Status);
            CREATE TABLE IF NOT EXISTS provider_reviews (
                Id TEXT NOT NULL PRIMARY KEY, FacilityId TEXT NOT NULL, UserId TEXT NOT NULL, Rating INTEGER NULL,
                ReviewText TEXT NOT NULL, VisitDate TEXT NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (FacilityId) REFERENCES facilities (Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_provider_reviews_FacilityId_Status_CreatedAt ON provider_reviews (FacilityId, Status, CreatedAt);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_provider_reviews_UserId_FacilityId ON provider_reviews (UserId, FacilityId);
            CREATE TABLE IF NOT EXISTS user_blocks (
                BlockerId TEXT NOT NULL, BlockedId TEXT NOT NULL, Kind TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                PRIMARY KEY (BlockerId, BlockedId, Kind),
                FOREIGN KEY (BlockerId) REFERENCES users (Id) ON DELETE CASCADE,
                FOREIGN KEY (BlockedId) REFERENCES users (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_user_blocks_BlockedId ON user_blocks (BlockedId);
            CREATE TABLE IF NOT EXISTS moderation_appeals (
                Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, ActionId TEXT NOT NULL, Reason TEXT NOT NULL,
                Status TEXT NOT NULL, DecisionNote TEXT NULL, ReviewedBy TEXT NULL, CreatedAt TEXT NOT NULL, ReviewedAt TEXT NULL,
                FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
                FOREIGN KEY (ActionId) REFERENCES moderation_actions (Id) ON DELETE CASCADE,
                FOREIGN KEY (ReviewedBy) REFERENCES users (Id) ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS IX_moderation_appeals_Status_CreatedAt ON moderation_appeals (Status, CreatedAt);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_moderation_appeals_UserId_ActionId ON moderation_appeals (UserId, ActionId);
            CREATE INDEX IF NOT EXISTS IX_moderation_appeals_ActionId ON moderation_appeals (ActionId);
            CREATE INDEX IF NOT EXISTS IX_moderation_appeals_ReviewedBy ON moderation_appeals (ReviewedBy);
            """);
    }
    else await db.Database.MigrateAsync();
    await PeerCareSeeder.SeedAsync(scope.ServiceProvider, app.Configuration, app.Environment);
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "PeerCare API" }));
app.MapGet("/api/auth/csrf", (IAntiforgery antiforgery, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store, no-cache";
    context.Response.Headers.Pragma = "no-cache";
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Antiforgery")
        .LogInformation("CSRF token issued. Authenticated={Authenticated}, AuthCookiePresent={AuthCookiePresent}",
            context.User.Identity?.IsAuthenticated == true, context.Request.Cookies.ContainsKey("peercare.auth"));
    return Results.Ok(new { token = tokens.RequestToken });
}).RequireAuthorization();
app.MapPeerCareAuth();
app.MapPeerCareAccount();
app.MapPeerCareHealth();
app.MapPeerCareContent();
app.MapPeerCareDirectory();
app.MapPeerCareCommunities();
app.MapPeerCareCommunityAdmin();
app.MapPeerCareUserSafety();
app.MapPeerCareAppeals();
app.MapPeerCareProfessional();
app.MapPeerCareAdmin();
app.MapPeerCareSeo();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program { }
