using Microsoft.EntityFrameworkCore;
using PeerCare.Data;
using PeerCare.Domain;

namespace PeerCare.Api;

public static class UserSafetyEndpoints
{
    public static void MapPeerCareUserSafety(this WebApplication app)
    {
        app.MapGet("/api/me/relationships", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var ids = await db.UserBlocks.Where(b => b.BlockerId == userId).ToListAsync(ct);
            var profiles = await db.Users.Where(u => ids.Select(x => x.BlockedId).Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
            return Results.Ok(ids.Select(x => new { x.BlockedId, x.Kind, x.CreatedAt, displayName = profiles.FirstOrDefault(p => p.Id == x.BlockedId)?.DisplayName }));
        }).RequireAuthorization();

        app.MapPost("/api/users/{id:guid}/{kind}", async (Guid id, string kind, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            if (kind is not ("block" or "mute")) return Results.BadRequest(new { message = "Choose block or mute." });
            if (id == userId || !await db.Users.AnyAsync(u => u.Id == id && u.Status == "active", ct)) return Results.BadRequest(new { message = "That member cannot be selected." });
            if (!await db.UserBlocks.AnyAsync(b => b.BlockerId == userId && b.BlockedId == id && b.Kind == kind, ct))
                db.UserBlocks.Add(new UserBlock { BlockerId = userId.Value, BlockedId = id, Kind = kind });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { active = true, kind });
        }).RequireAuthorization();

        app.MapDelete("/api/users/{id:guid}/{kind}", async (Guid id, string kind, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var item = await db.UserBlocks.FindAsync([userId.Value, id, kind], ct);
            if (item is not null) { db.UserBlocks.Remove(item); await db.SaveChangesAsync(ct); }
            return Results.Ok(new { active = false, kind });
        }).RequireAuthorization();
    }
}
