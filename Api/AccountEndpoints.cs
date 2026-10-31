using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Api;

public static class AccountEndpoints
{
    public static void MapPeerCareAccount(this WebApplication app)
    {
        app.MapPut("/api/me/onboarding", async (OnboardingRequest request, HttpContext http, PeerCareDbContext db,
            UserManager<PeerUser> users, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            var name = request.DisplayName.Trim();
            if (name.Length is < 2 or > 80) return Results.BadRequest(new { message = "Choose a display name between 2 and 80 characters." });
            user.DisplayName = name;
            user.State = CleanLocation(request.State, "Lagos");
            user.City = CleanLocation(request.City, "Lagos");
            var selected = (request.Conditions ?? []).Distinct().ToArray();
            var current = await db.UserConditions.Where(x => x.UserId == user.Id).ToListAsync(ct);
            db.UserConditions.RemoveRange(current);
            foreach (var conditionId in selected)
                if (await db.Conditions.AnyAsync(c => c.Id == conditionId && c.Active, ct))
                    db.UserConditions.Add(new UserCondition { UserId = user.Id, ConditionId = conditionId });
            await users.UpdateAsync(user);
            return Results.Ok(new { saved = true });
        }).RequireAuthorization();

        app.MapPost("/api/me/consent", async (ConsentRequest request, HttpContext http, PeerCareDbContext db,
            UserManager<PeerUser> users, SignInManager<PeerUser> signIn, IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!request.Accepted)
            {
                if (user.ConsentedAt is null && !await db.Posts.AnyAsync(p => p.UserId == user.Id, ct))
                    await users.DeleteAsync(user);
                else
                {
                    user.Status = "deleting";
                    user.DeleteRequestedAt = DateTimeOffset.UtcNow;
                    user.EmailNotifications = false;
                    await users.UpdateAsync(user);
                }
                await signIn.SignOutAsync();
                return Results.Ok(new { declined = true });
            }
            if (string.IsNullOrWhiteSpace(user.DisplayName))
                return Results.BadRequest(new { message = "Choose a display name before accepting consent." });
            var version = config["Consent:Version"] ?? "2026-10-v1";
            user.ConsentVersion = version;
            user.ConsentedAt = DateTimeOffset.UtcNow;
            user.InAppNotifications = request.InAppNotifications;
            user.EmailNotifications = request.EmailNotifications && user.EmailConfirmed;
            if (!await db.Consents.AnyAsync(c => c.UserId == user.Id && c.Version == version, ct))
                db.Consents.Add(new PeerConsent { UserId = user.Id, Version = version, AcceptedAt = user.ConsentedAt.Value });
            await users.UpdateAsync(user);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { accepted = true, version });
        }).RequireAuthorization();

        app.MapGet("/api/me/posts", async (HttpContext http, PeerCareDbContext db, UserManager<PeerUser> users, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            var posts = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => p.UserId == user.Id).OrderByDescending(p => p.CreatedAt), ct);
            var roles = await users.GetRolesAsync(user);
            var cards = new List<object>();
            foreach (var post in posts) cards.Add(await ApiSupport.PostCardAsync(db, post, user.Id, includePrivateStatus: true, ct: ct));
            return Results.Ok(new { user = ApiSupport.UserCard(user, roles), posts = cards });
        }).RequireAuthorization();

        app.MapGet("/api/me/saves", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            var ids = await db.Saves.Where(s => s.UserId == user.Id).OrderByDescending(s => s.CreatedAt).Select(s => s.PostId).ToListAsync(ct);
            var posts = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => ids.Contains(p.Id) && p.Status == "published"), ct);
            posts = posts.OrderBy(p => ids.IndexOf(p.Id)).ToList();
            var cards = new List<object>();
            foreach (var post in posts) cards.Add(await ApiSupport.PostCardAsync(db, post, user.Id, ct: ct));
            return Results.Ok(cards);
        }).RequireAuthorization();

        app.MapGet("/api/me/notifications", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == user.Id).OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(ct);
            return Results.Ok(items.Select(n => new { n.Id, n.Kind, payload = JsonSerializer.Deserialize<JsonElement>(n.PayloadJson), n.ReadAt, n.CreatedAt }));
        }).RequireAuthorization();

        app.MapPost("/api/me/notifications/{id:guid}/read", async (Guid id, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var item = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
            if (item is null) return Results.NotFound();
            item.ReadAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { read = true });
        }).RequireAuthorization();

        app.MapPost("/api/me/notifications/read-all", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);
            return Results.Ok(new { read = true });
        }).RequireAuthorization();

        app.MapPut("/api/me/settings", async (SettingsRequest request, HttpContext http, PeerCareDbContext db,
            UserManager<PeerUser> users, IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the health data consent before changing account settings." });
            if (request.DisplayName.Trim().Length is < 2 or > 80) return Results.BadRequest(new { message = "Choose a display name between 2 and 80 characters." });
            user.DisplayName = request.DisplayName.Trim();
            user.Bio = request.Bio?.Trim() is { Length: > 0 } bio ? bio[..Math.Min(bio.Length, 500)] : null;
            if (request.ProfileVisibility is not null)
            {
                if (request.ProfileVisibility is not ("public" or "private")) return Results.BadRequest(new { message = "Choose public or private profile visibility." });
                user.ProfileVisibility = request.ProfileVisibility;
            }
            user.State = CleanLocation(request.State, "Lagos");
            user.City = CleanLocation(request.City, "Lagos");
            user.InAppNotifications = request.InAppNotifications;
            user.EmailNotifications = request.EmailNotifications && user.EmailConfirmed;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(new { saved = true }) : Results.BadRequest(new { message = "Could not save settings." });
        }).RequireAuthorization();

        app.MapGet("/api/members/{id:guid}", async (Guid id, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.Status == "active", ct);
            var viewerId = ApiSupport.CurrentUserId(http.User);
            if (user is null || (user.ProfileVisibility != "public" && viewerId != user.Id)) return Results.NotFound();
            var posts = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => p.UserId == id && p.Status == "published").OrderByDescending(p => p.PublishedAt).Take(50), ct);
            var cards = new List<object>();
            foreach (var post in posts) cards.Add(await ApiSupport.PostCardAsync(db, post, viewerId, ct: ct));
            var communities = await db.Communities.AsNoTracking().Where(c => c.Status == "active" && db.CommunityMemberships.Any(m => m.CommunityId == c.Id && m.UserId == id && m.Status == "joined"))
                .Select(c => new { c.Name, c.Slug }).ToListAsync(ct);
            return Results.Ok(new { user.Id, user.DisplayName, user.Bio, posts = cards, communities });
        });

        app.MapGet("/api/me/export", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Results.NotFound();
            var posts = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => p.UserId == userId), ct);
            var replies = await db.Replies.AsNoTracking().Where(r => r.UserId == userId).Select(r => new { r.Id, r.PostId, r.Body, r.IsProfessionalNote, r.Status, r.CreatedAt }).ToListAsync(ct);
            var reportRows = await db.Reports.AsNoTracking().Where(r => r.ReporterId == userId).Select(r => new { r.Id, r.TargetType, r.TargetId, r.Reason, r.Status, r.CreatedAt }).ToListAsync(ct);
            var healthEntries = await db.HealthEntries.AsNoTracking().Where(e => e.UserId == userId && e.DeletedAt == null)
                .OrderBy(e => e.OccurredAt ?? e.CreatedAt).Select(e => new { e.Id, e.EntryType, e.Title, e.Description, e.OccurredAt, e.Severity, e.CreatedAt, e.UpdatedAt }).ToListAsync(ct);
            var joinedCommunities = await db.CommunityMemberships.AsNoTracking().Where(m => m.UserId == userId && m.Status == "joined")
                .Select(m => new { m.CommunityId, name = db.Communities.Where(c => c.Id == m.CommunityId).Select(c => c.Name).FirstOrDefault(), m.JoinedAt }).ToListAsync(ct);
            var result = new
            {
                exportedAt = DateTimeOffset.UtcNow,
                profile = new { user.DisplayName, user.Bio, user.ProfileVisibility, user.Email, user.PhoneNumber, user.State, user.City, user.ConsentVersion, user.ConsentedAt, user.CreatedAt },
                posts = posts.Select(p => new { p.Id, p.Kind, condition = p.Condition?.Name, p.AgeGroup, p.About, p.Title, p.Symptoms, p.DurationText, p.Story, p.Outcome, p.HelpNeeded, p.WhatFailed, p.WhatWorked, p.TimeToResolve, p.IsAnonymous, p.State, p.City, p.Status, p.CreatedAt }),
                replies,
                reports = reportRows,
                healthEntries,
                joinedCommunities
            };
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), "application/json", "peercare-data-export.json");
        }).RequireAuthorization();

        app.MapDelete("/api/me", async (HttpContext http, PeerCareDbContext db, UserManager<PeerUser> users,
            SignInManager<PeerUser> signIn, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            user.Status = "deleting";
            user.DeleteRequestedAt = DateTimeOffset.UtcNow;
            user.InAppNotifications = false;
            user.EmailNotifications = false;
            await db.Posts.Where(p => p.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "removed"), ct);
            await users.UpdateAsync(user);
            await signIn.SignOutAsync();
            return Results.Ok(new { deletionScheduled = true, completeWithinDays = 30 });
        }).RequireAuthorization();
    }

    private static string CleanLocation(string? value, string fallback)
    {
        var clean = value?.Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean.Length > 100 ? clean[..100] : clean;
    }
}
