using Microsoft.EntityFrameworkCore;
using PeerCare.Data;
using PeerCare.Domain;

namespace PeerCare.Api;

public static class CommunityEndpoints
{
    public static void MapPeerCareCommunities(this WebApplication app)
    {
        app.MapGet("/api/communities", async (string? q, PeerCareDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var viewerId = ApiSupport.CurrentUserId(http.User);
            var query = db.Communities.AsNoTracking().Where(c => c.Status == "active");
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(c => c.Name.ToLower().Contains(term) || c.Description.ToLower().Contains(term) || c.Condition!.Name.ToLower().Contains(term));
            }
            var items = await query.OrderBy(c => c.Name).Select(c => new
            {
                c.Id, c.Name, c.Slug, c.Description, c.Guidelines, condition = c.Condition == null ? null : c.Condition.Name,
                memberCount = db.CommunityMemberships.Count(m => m.CommunityId == c.Id && m.Status == "joined"),
                isJoined = viewerId != null && db.CommunityMemberships.Any(m => m.CommunityId == c.Id && m.UserId == viewerId && m.Status == "joined")
            }).Take(100).ToListAsync(ct);
            return Results.Ok(items);
        }).RequireRateLimiting("search");

        app.MapGet("/api/communities/{slug}", async (string slug, PeerCareDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var community = await db.Communities.AsNoTracking().Include(c => c.Condition).SingleOrDefaultAsync(c => c.Slug == slug && c.Status == "active", ct);
            if (community is null) return Results.NotFound();
            var viewerId = ApiSupport.CurrentUserId(http.User);
            var posts = new List<object>();
            if (community.ConditionId is not null)
            {
                var rows = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => p.Status == "published" && p.ConditionId == community.ConditionId)
                    .OrderByDescending(p => p.PublishedAt).Take(30), ct);
                foreach (var row in rows) posts.Add(await ApiSupport.PostCardAsync(db, row, viewerId, ct: ct));
            }
            var count = await db.CommunityMemberships.CountAsync(m => m.CommunityId == community.Id && m.Status == "joined", ct);
            var isJoined = viewerId is not null && await db.CommunityMemberships.AnyAsync(m => m.CommunityId == community.Id && m.UserId == viewerId && m.Status == "joined", ct);
            return Results.Ok(new { community.Id, community.Name, community.Slug, community.Description, community.Guidelines, condition = community.Condition?.Name, memberCount = count, isJoined, posts });
        });

        app.MapPost("/api/communities/{id:guid}/join", async (Guid id, HttpContext http, PeerCareDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the community and health-data terms before joining." });
            if (!await db.Communities.AnyAsync(c => c.Id == id && c.Status == "active", ct)) return Results.NotFound();
            var membership = await db.CommunityMemberships.FindAsync([id, user.Id], ct);
            if (membership is null) db.CommunityMemberships.Add(new CommunityMembership { CommunityId = id, UserId = user.Id });
            else membership.Status = "joined";
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { joined = true });
        }).RequireAuthorization().RequireRateLimiting("posting");

        app.MapDelete("/api/communities/{id:guid}/join", async (Guid id, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var membership = await db.CommunityMemberships.FindAsync([id, userId.Value], ct);
            if (membership is not null) { membership.Status = "left"; await db.SaveChangesAsync(ct); }
            return Results.Ok(new { joined = false });
        }).RequireAuthorization();
    }
}
