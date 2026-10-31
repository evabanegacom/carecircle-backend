using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Api;

public static class HealthEndpoints
{
    private static readonly string[] EntryTypes =
        ["symptom", "self_reported_condition", "treatment_note", "side_effect", "appointment", "wellbeing", "general_note"];

    public static void MapPeerCareHealth(this WebApplication app)
    {
        app.MapGet("/api/me/health-entries", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var entries = await db.HealthEntries.AsNoTracking()
                .Where(e => e.UserId == userId && e.DeletedAt == null)
                .OrderByDescending(e => e.OccurredAt ?? e.CreatedAt)
                .Select(e => new { e.Id, e.EntryType, e.Title, e.Description, e.OccurredAt, e.Severity, e.CreatedAt, e.UpdatedAt })
                .ToListAsync(ct);
            return Results.Ok(entries);
        }).RequireAuthorization();

        app.MapPost("/api/me/health-entries", async (HealthEntryRequest request, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var validation = Validate(request);
            if (validation is not null) return Results.BadRequest(new { message = validation });
            var entry = new HealthEntry
            {
                UserId = userId.Value,
                EntryType = request.EntryType.Trim().ToLowerInvariant(),
                Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
                Description = request.Description.Trim(),
                OccurredAt = request.OccurredAt,
                Severity = request.Severity
            };
            db.HealthEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/me/health-entries/{entry.Id}", new { entry.Id });
        }).RequireAuthorization();

        app.MapPatch("/api/me/health-entries/{id:guid}", async (Guid id, HealthEntryRequest request, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var entry = await db.HealthEntries.SingleOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.DeletedAt == null, ct);
            if (entry is null) return Results.NotFound();
            var validation = Validate(request);
            if (validation is not null) return Results.BadRequest(new { message = validation });
            entry.EntryType = request.EntryType.Trim().ToLowerInvariant();
            entry.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
            entry.Description = request.Description.Trim();
            entry.OccurredAt = request.OccurredAt;
            entry.Severity = request.Severity;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { saved = true });
        }).RequireAuthorization();

        app.MapDelete("/api/me/health-entries/{id:guid}", async (Guid id, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var entry = await db.HealthEntries.SingleOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.DeletedAt == null, ct);
            if (entry is null) return Results.NotFound();
            entry.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true });
        }).RequireAuthorization();

        app.MapPost("/api/me/health-entries/share", async (ShareHealthEntriesRequest request, HttpContext http,
            PeerCareDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the community terms before sharing entries." });
            if (!request.Confirmed) return Results.BadRequest(new { message = "Confirm the selected-entry preview before sharing." });
            var title = request.Title.Trim();
            if (title.Length is < 4 or > 180 || request.EntryIds.Length is < 1 or > 10) return Results.BadRequest(new { message = "Choose 1 to 10 entries and enter a clear title." });
            if (!await db.Conditions.AnyAsync(c => c.Id == request.ConditionId && c.Active, ct)) return Results.BadRequest(new { message = "Choose a topic." });
            var entries = await db.HealthEntries.AsNoTracking().Where(e => e.UserId == user.Id && e.DeletedAt == null && request.EntryIds.Contains(e.Id))
                .OrderBy(e => e.OccurredAt ?? e.CreatedAt).ToListAsync(ct);
            if (entries.Count != request.EntryIds.Distinct().Count()) return Results.BadRequest(new { message = "One or more selected entries are no longer available in your private timeline." });
            var story = "Selected from my private health timeline:\n\n" + string.Join("\n\n", entries.Select(e =>
                $"{e.OccurredAt?.ToString("yyyy-MM-dd") ?? "Date not recorded"} · {e.EntryType.Replace('_', ' ')}{(string.IsNullOrWhiteSpace(e.Title) ? "" : $"\n{e.Title}")}\n{e.Description}"));
            if (story.Length > 5000) return Results.BadRequest(new { message = "The selected notes are too long to share together. Select fewer entries." });
            var scan = SafetyScanner.Scan(title, story);
            if (scan.HasDose) return Results.UnprocessableEntity(new { message = "Remove medication amounts or schedules before sharing these entries." });
            var post = new PeerPost
            {
                UserId = user.Id, Kind = "experience", ConditionId = request.ConditionId, AgeGroup = "Not stated", About = "self",
                Title = title, Story = story, DiagnosisStatus = "not_stated", Outcome = "unknown", IsAnonymous = false,
                State = user.State ?? "Lagos", City = user.City ?? "Lagos", Status = "pending",
                EmergencyFlag = scan.Emergency, FlagsCount = scan.Flags.Count, AutoFlagsJson = System.Text.Json.JsonSerializer.Serialize(scan.Flags)
            };
            db.Posts.Add(post);
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new { post.Id, post.Status, message = "Your selected entries were submitted as a public experience for moderator review." });
        }).RequireAuthorization().RequireRateLimiting("posting");
    }

    private static string? Validate(HealthEntryRequest request)
    {
        var type = request.EntryType.Trim().ToLowerInvariant();
        if (!EntryTypes.Contains(type)) return "Choose a valid entry type.";
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 5000)
            return "Add a note between 1 and 5,000 characters.";
        if (request.Title?.Trim().Length > 160) return "Titles must be 160 characters or fewer.";
        if (request.Severity is < 1 or > 10) return "Severity must be between 1 and 10.";
        return null;
    }
}
