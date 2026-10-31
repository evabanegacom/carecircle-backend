using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Api;

public static class AppealEndpoints
{
    public static void MapPeerCareAppeals(this WebApplication app)
    {
        app.MapGet("/api/me/appeals", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            return Results.Ok(await db.ModerationAppeals.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.CreatedAt)
                .Select(a => new { a.Id, a.ActionId, a.Reason, a.Status, a.DecisionNote, a.CreatedAt, a.ReviewedAt }).ToListAsync(ct));
        }).RequireAuthorization();

        app.MapGet("/api/me/appealable-actions", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var actions = await db.ModerationActions.AsNoTracking()
                .Where(a => a.Action == "reject" || a.Action == "remove" || a.Action == "suspend")
                .OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);
            var existing = await db.ModerationAppeals.AsNoTracking().Where(a => a.UserId == userId).Select(a => a.ActionId).ToListAsync(ct);
            var eligible = new List<object>();
            foreach (var action in actions)
                if (!existing.Contains(action.Id) && await OwnsActionTarget(db, action, userId.Value, ct))
                    eligible.Add(new { action.Id, action.Action, action.TargetType, action.Reason, action.CreatedAt });
            return Results.Ok(eligible);
        }).RequireAuthorization();

        app.MapPost("/api/me/appeals", async (UserAppealRequest request, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var reason = request.Reason.Trim();
            if (reason.Length is < 10 or > 2000) return Results.BadRequest(new { message = "Explain your appeal in 10 to 2,000 characters." });
            var action = await db.ModerationActions.SingleOrDefaultAsync(a => a.Id == request.ActionId, ct);
            if (action is null || !await OwnsActionTarget(db, action, userId.Value, ct)) return Results.NotFound();
            if (await db.ModerationAppeals.AnyAsync(a => a.UserId == userId && a.ActionId == action.Id, ct))
                return Results.Conflict(new { message = "An appeal already exists for this action." });
            var appeal = new ModerationAppeal { UserId = userId.Value, ActionId = action.Id, Reason = reason };
            db.ModerationAppeals.Add(appeal);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/me/appeals/{appeal.Id}", new { appeal.Id, appeal.Status });
        }).RequireAuthorization().RequireRateLimiting("posting");

        app.MapGet("/api/admin/queue/appeals", async (PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ModerationAppeals.AsNoTracking().Include(a => a.User).Include(a => a.Action)
                .Where(a => a.Status == "open").OrderBy(a => a.CreatedAt)
                .Select(a => new { a.Id, a.ActionId, member = a.User!.DisplayName, a.Reason, action = a.Action!.Action, targetType = a.Action.TargetType, a.CreatedAt }).ToListAsync(ct)))
            .RequireAuthorization("Moderator");

        app.MapPost("/api/admin/appeals/{id:guid}/review", async (Guid id, AdminAppealRequest request, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var actor = ApiSupport.CurrentUserId(http.User);
            if (actor is null) return Results.Unauthorized();
            if (request.Status is not ("accepted" or "rejected")) return Results.BadRequest(new { message = "Choose accepted or rejected." });
            var appeal = await db.ModerationAppeals.Include(a => a.Action).SingleOrDefaultAsync(a => a.Id == id && a.Status == "open", ct);
            if (appeal is null) return Results.NotFound();
            appeal.Status = request.Status;
            appeal.DecisionNote = request.DecisionNote?.Trim() is { Length: > 0 } note ? note[..Math.Min(note.Length, 2000)] : null;
            appeal.ReviewedBy = actor;
            appeal.ReviewedAt = DateTimeOffset.UtcNow;
            if (request.Status == "accepted")
                await ReverseAction(db, appeal.Action!, ct);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(actor, $"appeal_{request.Status}", "appeal", id, ct);
            return Results.Ok(new { appeal.Id, appeal.Status });
        }).RequireAuthorization("Moderator");
    }

    private static async Task<bool> OwnsActionTarget(PeerCareDbContext db, ModerationAction action, Guid userId, CancellationToken ct)
    {
        var type = action.TargetType;
        var id = action.TargetId;
        if (type == "report")
        {
            var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (report is null) return false;
            type = report.TargetType;
            id = report.TargetId;
        }
        return type switch
        {
            "post" => await db.Posts.AnyAsync(p => p.Id == id && p.UserId == userId, ct),
            "reply" => await db.Replies.AnyAsync(r => r.Id == id && r.UserId == userId, ct),
            "review" => await db.ProviderReviews.AnyAsync(r => r.Id == id && r.UserId == userId, ct),
            "user" => id == userId,
            _ => false
        };
    }

    private static async Task ReverseAction(PeerCareDbContext db, ModerationAction action, CancellationToken ct)
    {
        var type = action.TargetType;
        var id = action.TargetId;
        if (type == "report")
        {
            var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (report is null) return;
            type = report.TargetType;
            id = report.TargetId;
        }
        if (type == "post")
        {
            var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (post is not null) { post.Status = "published"; post.PublishedAt ??= DateTimeOffset.UtcNow; }
        }
        else if (type == "reply")
        {
            var reply = await db.Replies.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (reply is not null) reply.Status = "published";
        }
        else if (type == "review")
        {
            var review = await db.ProviderReviews.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (review is not null) review.Status = "approved";
        }
        else if (type == "user")
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is not null && user.Status == "suspended") user.Status = "active";
        }
    }
}
