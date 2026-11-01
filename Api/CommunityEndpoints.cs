using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

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

    public static void MapPeerCareCommunityAdmin(this WebApplication app)
    {
        app.MapGet("/api/admin/communities", async (PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Communities.AsNoTracking().OrderBy(c => c.Name).Select(c => new
            {
                c.Id, c.Name, c.Slug, c.Description, c.Guidelines, c.Status, c.ConditionId,
                condition = c.Condition == null ? null : c.Condition.Name,
                memberCount = db.CommunityMemberships.Count(m => m.CommunityId == c.Id && m.Status == "joined")
            }).ToListAsync(ct))).RequireAuthorization("Admin");

        app.MapPost("/api/admin/communities", async (AdminCommunityRequest request, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var name = request.Name.Trim();
            var slug = NormalizeCommunitySlug(request.Slug);
            var description = request.Description.Trim();
            var guidelines = request.Guidelines.Trim();
            var status = request.Status.Trim().ToLowerInvariant();
            if (name.Length is < 2 or > 120 || slug.Length is < 2 or > 120 || description.Length > 1000 ||
                guidelines.Length > 3000 || status is not ("active" or "inactive"))
                return Results.BadRequest(new { message = "Check the community name, slug, description, guidelines, and status." });
            if (await db.Communities.AnyAsync(c => c.Slug == slug, ct))
                return Results.Conflict(new { message = "A community with that URL slug already exists." });
            if (request.ConditionId is not null && !await db.Conditions.AnyAsync(c => c.Id == request.ConditionId, ct))
                return Results.BadRequest(new { message = "Choose a valid topic or leave it unassigned." });

            var community = new Community { Name = name, Slug = slug, Description = description, Guidelines = guidelines, Status = status, ConditionId = request.ConditionId };
            db.Communities.Add(community);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(ApiSupport.CurrentUserId(http.User), "community_created", "community", community.Id, ct);
            return Results.Created($"/api/admin/communities/{community.Id}", new { community.Id, community.Name, community.Slug, community.Description, community.Guidelines, community.Status, community.ConditionId });
        }).RequireAuthorization("Admin");

        app.MapPut("/api/admin/communities/{id:guid}", async (Guid id, AdminCommunityRequest request, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var community = await db.Communities.SingleOrDefaultAsync(c => c.Id == id, ct);
            if (community is null) return Results.NotFound();
            var name = request.Name.Trim();
            var slug = NormalizeCommunitySlug(request.Slug);
            var description = request.Description.Trim();
            var guidelines = request.Guidelines.Trim();
            var status = request.Status.Trim().ToLowerInvariant();
            if (name.Length is < 2 or > 120 || slug.Length is < 2 or > 120 || description.Length > 1000 ||
                guidelines.Length > 3000 || status is not ("active" or "inactive"))
                return Results.BadRequest(new { message = "Check the community name, slug, description, guidelines, and status." });
            if (await db.Communities.AnyAsync(c => c.Id != id && c.Slug == slug, ct))
                return Results.Conflict(new { message = "A community with that URL slug already exists." });
            if (request.ConditionId is not null && !await db.Conditions.AnyAsync(c => c.Id == request.ConditionId, ct))
                return Results.BadRequest(new { message = "Choose a valid topic or leave it unassigned." });

            community.Name = name;
            community.Slug = slug;
            community.Description = description;
            community.Guidelines = guidelines;
            community.Status = status;
            community.ConditionId = request.ConditionId;
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(ApiSupport.CurrentUserId(http.User), "community_updated", "community", community.Id, ct);
            return Results.Ok(new { community.Id, community.Name, community.Slug, community.Description, community.Guidelines, community.Status, community.ConditionId });
        }).RequireAuthorization("Admin");

        app.MapDelete("/api/admin/communities/{id:guid}", async (Guid id, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var community = await db.Communities.SingleOrDefaultAsync(c => c.Id == id, ct);
            if (community is null) return Results.NotFound();
            community.Status = "inactive";
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(ApiSupport.CurrentUserId(http.User), "community_deactivated", "community", id, ct);
            return Results.Ok(new { community.Id, community.Status });
        }).RequireAuthorization("Admin");
    }

    private static string NormalizeCommunitySlug(string value)
    {
        var slug = new string(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug;
    }
}
