using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Api;

public static class DirectoryEndpoints
{
    public static void MapPeerCareDirectory(this WebApplication app)
    {
        app.MapGet("/api/facilities", async (string? q, string? city, string? state, string? specialty, PeerCareDbContext db, CancellationToken ct) =>
        {
            var query = db.Facilities.AsNoTracking().Where(f => f.Status == "verified");
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(f => f.Name.ToLower().Contains(q.Trim().ToLower()));
            if (!string.IsNullOrWhiteSpace(city)) query = query.Where(f => f.City.ToLower() == city.Trim().ToLower());
            if (!string.IsNullOrWhiteSpace(state)) query = query.Where(f => f.State.ToLower() == state.Trim().ToLower());
            if (!string.IsNullOrWhiteSpace(specialty)) query = query.Where(f => db.Practitioners.Any(p => p.FacilityId == f.Id && p.Status == "visible" && p.Specialty != null && p.Specialty.ToLower().Contains(specialty.Trim().ToLower())));
            var items = await query.OrderBy(f => f.Name).Take(40).Select(f => new { f.Id, f.Name, f.Type, f.City, f.State, f.Source, f.LastVerifiedAt }).ToListAsync(ct);
            return Results.Ok(items);
        }).RequireRateLimiting("search");

        app.MapGet("/api/facilities/{id:guid}", async (Guid id, PeerCareDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id && f.Status == "verified", ct);
            if (facility is null) return Results.NotFound();
            var linked = db.PostFacilities.AsNoTracking().Where(x => x.FacilityId == id && x.Post != null && x.Post.Status == "published");
            var postIds = await linked.Select(x => x.PostId).Distinct().ToListAsync(ct);
            var posts = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => postIds.Contains(p.Id) && p.Status == "published").OrderByDescending(p => p.PublishedAt), ct);
            var viewer = ApiSupport.CurrentUserId(http.User);
            var cards = new List<object>();
            foreach (var post in posts) cards.Add(await ApiSupport.PostCardAsync(db, post, viewer, ct: ct));
            var relations = await linked.Select(x => new { x.Result, x.Post!.ConditionId, condition = x.Post.Condition!.Name, x.Post.UserId }).ToListAsync(ct);
            var uniqueAuthors = relations.Select(x => x.UserId).Distinct().Count();
            var practitioners = await db.Practitioners.AsNoTracking()
                .Where(p => p.FacilityId == id && p.Status == "visible" && p.ReportCount >= 5)
                .Select(p => new { p.Name, p.Specialty }).ToListAsync(ct);
            return Results.Ok(new
            {
                facility = new { facility.Id, facility.Name, facility.Type, facility.Address, facility.City, facility.State, facility.Source, facility.LastVerifiedAt },
                summary = new
                {
                    experienceCount = postIds.Count,
                    independentReports = uniqueAuthors,
                    conditions = relations.Select(x => x.condition).Distinct().Order().ToList(),
                    helped = relations.Count(x => x.Result == "helped_us"),
                    noChange = relations.Count(x => x.Result == "no_change"),
                    madeWorse = relations.Count(x => x.Result == "made_worse")
                },
                practitioners,
                reviews = await db.ProviderReviews.AsNoTracking().Where(r => r.FacilityId == id && r.Status == "approved")
                    .OrderByDescending(r => r.CreatedAt).Take(50).Select(r => new { r.Id, r.Rating, r.ReviewText, r.VisitDate, r.CreatedAt, author = db.Users.Where(u => u.Id == r.UserId).Select(u => u.DisplayName).FirstOrDefault() }).ToListAsync(ct),
                experiences = cards
            });
        });

        app.MapPost("/api/facilities/{id:guid}/reviews", async (Guid id, ProviderReviewRequest request, HttpContext http, PeerCareDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the community terms before submitting a review." });
            if (request.Rating is < 1 or > 5) return Results.BadRequest(new { message = "Rating must be from 1 to 5, or left blank." });
            var text = request.ReviewText.Trim();
            if (text.Length is < 20 or > 3000) return Results.BadRequest(new { message = "Write a review between 20 and 3,000 characters." });
            if (!await db.Facilities.AnyAsync(f => f.Id == id && f.Status == "verified", ct)) return Results.NotFound();
            var scan = SafetyScanner.Scan(text);
            if (scan.HasDose) return Results.UnprocessableEntity(new { message = "Remove medication amounts and dose schedules before submitting." });
            var review = await db.ProviderReviews.SingleOrDefaultAsync(r => r.FacilityId == id && r.UserId == user.Id, ct);
            if (review is null) { review = new ProviderReview { FacilityId = id, UserId = user.Id }; db.ProviderReviews.Add(review); }
            review.Rating = request.Rating;
            review.ReviewText = text;
            review.VisitDate = request.VisitDate;
            review.Status = "pending";
            review.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new { review.Id, review.Status, message = "Your review is waiting for moderation." });
        }).RequireAuthorization().RequireRateLimiting("posting");

        app.MapDelete("/api/provider-reviews/{id:guid}", async (Guid id, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var review = await db.ProviderReviews.SingleOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
            if (review is null) return Results.NotFound();
            review.Status = "removed";
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { removed = true });
        }).RequireAuthorization();

        app.MapPost("/api/admin/facilities/{id:guid}/review", async (Guid id, string status, FacilityVerificationRequest request, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var actor = ApiSupport.CurrentUserId(http.User);
            if (actor is null) return Results.Unauthorized();
            if (status is not ("verified" or "rejected")) return Results.BadRequest(new { message = "Choose verified or rejected." });
            var facility = await db.Facilities.SingleOrDefaultAsync(f => f.Id == id, ct);
            if (facility is null) return Results.NotFound();
            if (status == "verified")
            {
                var source = request.Source?.Trim();
                if (string.IsNullOrWhiteSpace(source) || source.Length > 300 || request.LastVerifiedAt is null)
                    return Results.BadRequest(new { message = "A source and verification date are required before a facility can be verified." });
                facility.Source = source;
                facility.LastVerifiedAt = request.LastVerifiedAt.Value;
            }
            facility.Status = status;
            db.ModerationActions.Add(new ModerationAction { ModeratorId = actor.Value, TargetType = "facility", TargetId = id, Action = status, Reason = status == "verified" ? $"Verified against {facility.Source}" : "Facility rejected." });
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(actor, $"facility_{status}", "facility", id, ct);
            return Results.Ok(new { facility.Id, facility.Status, facility.Source, facility.LastVerifiedAt });
        }).RequireAuthorization("Moderator");

        app.MapGet("/api/admin/queue/reviews", async (PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ProviderReviews.AsNoTracking().Include(r => r.User).Where(r => r.Status == "pending").OrderBy(r => r.CreatedAt)
                .Select(r => new { r.Id, r.FacilityId, facility = db.Facilities.Where(f => f.Id == r.FacilityId).Select(f => f.Name).FirstOrDefault(), author = r.User!.DisplayName, r.Rating, r.ReviewText, r.VisitDate, r.CreatedAt }).ToListAsync(ct)))
            .RequireAuthorization("Moderator");

        app.MapPost("/api/admin/provider-reviews/{id:guid}/review", async (Guid id, AdminVerificationRequest request, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var actor = ApiSupport.CurrentUserId(http.User);
            if (actor is null) return Results.Unauthorized();
            if (request.Status is not ("approved" or "rejected")) return Results.BadRequest(new { message = "Choose approved or rejected." });
            var review = await db.ProviderReviews.SingleOrDefaultAsync(r => r.Id == id && r.Status == "pending", ct);
            if (review is null) return Results.NotFound();
            review.Status = request.Status;
            review.UpdatedAt = DateTimeOffset.UtcNow;
            db.ModerationActions.Add(new ModerationAction { ModeratorId = actor.Value, TargetType = "review", TargetId = id, Action = request.Status, Reason = request.RejectReason?.Trim() ?? "Provider review moderation." });
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(actor, $"provider_review_{request.Status}", "provider_review", id, ct);
            return Results.Ok(new { review.Id, review.Status });
        }).RequireAuthorization("Moderator");

        app.MapPost("/api/facilities/{id:guid}/issues", async (Guid id, FacilityIssueRequest request, HttpContext http,
            PeerCare.Data.PeerCareDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the health data consent before submitting a correction." });
            if (request.Detail.Trim().Length < 10 || request.Detail.Length > 2000) return Results.BadRequest(new { message = "Tell us what needs correcting (10 to 2,000 characters)." });
            if (!await db.Facilities.AnyAsync(f => f.Id == id && f.Status == "verified", ct)) return Results.NotFound();
            db.FacilityIssues.Add(new PeerCare.Domain.FacilityIssue { FacilityId = id, ReporterId = user.Id, Detail = request.Detail.Trim() });
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new { received = true, responseWithinDays = 7 });
        }).RequireAuthorization().RequireRateLimiting("posting");
    }
}
