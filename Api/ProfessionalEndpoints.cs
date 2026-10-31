using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Api;

public static class ProfessionalEndpoints
{
    public static void MapPeerCareProfessional(this WebApplication app)
    {
        app.MapGet("/api/me/professional-verification", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var item = await db.ProfessionalVerifications.AsNoTracking().Where(v => v.UserId == userId)
                .OrderByDescending(v => v.ReviewedAt).ThenByDescending(v => v.Id)
                .Select(v => new { v.Id, v.Profession, v.IssuingBody, v.Status, v.RejectReason, v.ReviewedAt })
                .FirstOrDefaultAsync(ct);
            return Results.Ok(item);
        }).RequireAuthorization();

        app.MapPost("/api/me/professional-verification", async (ProfessionalVerificationRequest request, HttpContext http,
            PeerCareDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the health data consent before requesting verification." });
            if (request.Profession.Trim().Length is < 2 or > 100 || request.IssuingBody.Trim().Length is < 2 or > 180 || request.LicenceNumber.Trim().Length is < 2 or > 120)
                return Results.BadRequest(new { message = "Add your profession, licence number, and issuing body." });
            if (await db.ProfessionalVerifications.AnyAsync(v => v.UserId == user.Id && v.Status == "pending", ct))
                return Results.Conflict(new { message = "You already have a verification request waiting for review." });
            db.ProfessionalVerifications.Add(new ProfessionalVerification
            {
                UserId = user.Id, Profession = request.Profession.Trim(), LicenceNumber = request.LicenceNumber.Trim(),
                IssuingBody = request.IssuingBody.Trim(), Status = "pending"
            });
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new { status = "pending", message = "Your licence details are waiting for admin review." });
        }).RequireAuthorization().RequireRateLimiting("posting");

        app.MapGet("/api/me/professional-notes", async (HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(await db.Replies.AsNoTracking().Where(r => r.UserId == user.Id && r.IsProfessionalNote)
                .OrderByDescending(r => r.CreatedAt).Select(r => new { r.Id, r.PostId, r.Body, r.Status, r.CreatedAt }).ToListAsync(ct));
        }).RequireAuthorization();
    }
}
