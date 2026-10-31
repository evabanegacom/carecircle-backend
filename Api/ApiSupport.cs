using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeerCare.Data;
using PeerCare.Domain;

namespace PeerCare.Api;

public static class ApiSupport
{
    public static Guid? CurrentUserId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static async Task<PeerUser?> CurrentUserAsync(ClaimsPrincipal principal, PeerCareDbContext db, CancellationToken ct)
    {
        var id = CurrentUserId(principal);
        if (id is null) return null;
        return await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Status == "active" && u.DeleteRequestedAt == null, ct);
    }

    public static bool HasConsent(PeerUser user, IConfiguration configuration)
        => user.ConsentedAt is not null && user.ConsentVersion == (configuration["Consent:Version"] ?? "2026-10-v1");

    public static object UserCard(PeerUser user, IEnumerable<string> roles) => new
    {
        id = user.Id,
        displayName = user.DisplayName,
        state = user.State,
        city = user.City,
        status = user.Status,
        roles,
        isProfessionalVerified = roles.Contains("Professional"),
        bio = user.Bio,
        profileVisibility = user.ProfileVisibility,
        needsOnboarding = string.IsNullOrWhiteSpace(user.DisplayName),
        consentVersion = user.ConsentVersion,
        emailNotifications = user.EmailNotifications,
        inAppNotifications = user.InAppNotifications
    };

    public static async Task<object> PostCardAsync(PeerCareDbContext db, PeerPost post, Guid? viewerId, bool includePrivateStatus = false, CancellationToken ct = default)
    {
        var help = await db.Reactions.CountAsync(r => r.TargetType == "post" && r.TargetId == post.Id && r.Kind == "helped_me_too", ct);
        var thanks = await db.Reactions.CountAsync(r => r.TargetType == "post" && r.TargetId == post.Id && r.Kind == "thank_you", ct);
        var replies = await db.Replies.CountAsync(r => r.PostId == post.Id && r.Status == "published", ct);
        var hasHelped = viewerId is not null && await db.Reactions.AnyAsync(r => r.UserId == viewerId && r.TargetType == "post" && r.TargetId == post.Id && r.Kind == "helped_me_too", ct);
        var hasThanked = viewerId is not null && await db.Reactions.AnyAsync(r => r.UserId == viewerId && r.TargetType == "post" && r.TargetId == post.Id && r.Kind == "thank_you", ct);
        var saved = viewerId is not null && await db.Saves.AnyAsync(s => s.UserId == viewerId && s.PostId == post.Id, ct);
        var publicFacilities = post.Facilities.Where(f => f.Facility is not null).Select(f => new
        {
            id = f.Facility!.Id,
            name = f.Facility.Name,
            city = f.Facility.City,
            state = f.Facility.State,
            verified = f.Facility.Status == "verified",
            result = f.Result,
            practitioner = f.Practitioner?.Status == "visible" ? f.Practitioner.Name : null
        });
        var dto = new Dictionary<string, object?>
        {
            ["id"] = post.Id,
            ["kind"] = post.Kind,
            ["condition"] = post.Condition?.Name,
            ["conditionId"] = post.ConditionId,
            ["ageGroup"] = post.AgeGroup,
            ["about"] = post.About,
            ["title"] = post.Title,
            ["symptoms"] = post.Symptoms,
            ["durationText"] = post.DurationText,
            ["story"] = post.Story,
            ["outcome"] = post.Outcome,
            ["diagnosisStatus"] = post.DiagnosisStatus,
            ["helpNeeded"] = post.HelpNeeded,
            ["whatFailed"] = post.WhatFailed,
            ["whatWorked"] = post.WhatWorked,
            ["timeToResolve"] = post.TimeToResolve,
            ["isAnonymous"] = post.IsAnonymous,
            ["author"] = post.IsAnonymous ? "Anonymous" : post.User?.DisplayName ?? "Member",
            ["authorId"] = !post.IsAnonymous && post.User?.ProfileVisibility == "public" ? post.UserId : null,
            ["state"] = post.State,
            ["city"] = post.City,
            ["status"] = includePrivateStatus ? post.Status : null,
            ["emergency"] = post.EmergencyFlag,
            ["flags"] = JsonSerializer.Deserialize<string[]>(post.AutoFlagsJson) ?? [],
            ["treatments"] = post.Treatments.Select(t => new { id = t.TreatmentId, name = t.Treatment?.Name ?? "Treatment", kind = t.Treatment?.Kind ?? "other", outcome = t.Outcome, note = t.Note }),
            ["facilities"] = publicFacilities,
            ["hospital"] = publicFacilities.FirstOrDefault()?.name,
            ["helpedCount"] = help,
            ["thankYouCount"] = thanks,
            ["replyCount"] = replies,
            ["hasHelped"] = hasHelped,
            ["hasThanked"] = hasThanked,
            ["isSaved"] = saved,
            ["createdAt"] = post.CreatedAt,
            ["publishedAt"] = post.PublishedAt
        };
        return dto;
    }

    public static async Task<List<PeerPost>> LoadPostsAsync(IQueryable<PeerPost> query, CancellationToken ct)
        => await query.AsNoTracking()
            .Include(p => p.Condition)
            .Include(p => p.User)
            .Include(p => p.Treatments).ThenInclude(t => t.Treatment)
            .Include(p => p.Facilities).ThenInclude(f => f.Facility)
            .Include(p => p.Facilities).ThenInclude(f => f.Practitioner)
            .ToListAsync(ct);

    public static async Task<bool> IsVerifiedProfessionalAsync(PeerCareDbContext db, PeerUser user, CancellationToken ct)
        => await db.ProfessionalVerifications.AnyAsync(v => v.UserId == user.Id && v.Status == "approved", ct);
}
