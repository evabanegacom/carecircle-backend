using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Api;

public static class AdminEndpoints
{
    public static void MapPeerCareAdmin(this WebApplication app)
    {
        app.MapGet("/api/admin/queue", async (PeerCareDbContext db, CancellationToken ct) => Results.Ok(new
        {
            pendingPosts = await db.Posts.CountAsync(p => p.Status == "pending", ct),
            openReports = await db.Reports.CountAsync(r => r.Status == "open", ct),
            verificationRequests = await db.ProfessionalVerifications.CountAsync(v => v.Status == "pending", ct),
            newFacilities = await db.Facilities.CountAsync(f => f.Status == "unverified", ct),
            facilityIssues = await db.FacilityIssues.CountAsync(i => i.Status == "open", ct)
        })).RequireAuthorization("Moderator");

        app.MapGet("/api/admin/queue/posts", async (PeerCareDbContext db, CancellationToken ct) =>
        {
            var posts = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => p.Status == "pending").OrderBy(p => p.CreatedAt), ct);
            var items = posts.Select(p => new
            {
                p.Id, p.Kind, condition = p.Condition!.Name, p.AgeGroup, p.About, p.Title, p.Symptoms, p.DurationText, p.Story,
                p.Outcome, p.HelpNeeded, p.WhatFailed, p.WhatWorked, p.TimeToResolve, author = p.IsAnonymous ? "Anonymous" : p.User!.DisplayName,
                p.IsAnonymous, p.EmergencyFlag, flags = System.Text.Json.JsonSerializer.Deserialize<string[]>(p.AutoFlagsJson) ?? [],
                treatments = p.Treatments.Select(t => new { name = t.Treatment!.Name, t.Outcome, t.Note }),
                facilities = p.Facilities.Select(f => new { name = f.Facility!.Name, f.Result, f.Facility.Status }), p.CreatedAt
            });
            return Results.Ok(items);
        }).RequireAuthorization("Moderator");

        app.MapPost("/api/admin/posts/{id:guid}/action", async (Guid id, AdminPostActionRequest request, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, PeerNotificationService notifications, CancellationToken ct) =>
        {
            var moderatorId = ApiSupport.CurrentUserId(http.User);
            if (moderatorId is null) return Results.Unauthorized();
            var post = await db.Posts.Include(p => p.User).SingleOrDefaultAsync(p => p.Id == id, ct);
            if (post is null) return Results.NotFound();
            var action = request.Action.Trim().ToLowerInvariant();
            if (action is not ("approve" or "reject" or "edit" or "remove" or "escalate"))
                return Results.BadRequest(new { message = "Choose an available moderation action." });
            if (action == "edit")
            {
                if (string.IsNullOrWhiteSpace(request.CleanedStory) || request.CleanedStory.Length > 5000)
                    return Results.BadRequest(new { message = "Add the cleaned story text to save the edit." });
                post.Story = request.CleanedStory.Trim();
                if (request.CleanedSymptoms is not null) post.Symptoms = request.CleanedSymptoms.Trim();
                var scan = SafetyScanner.Scan(post.Title, post.Symptoms, post.DurationText, post.Story, post.WhatFailed, post.WhatWorked);
                if (scan.HasDose) return Results.UnprocessableEntity(new { message = "The edited story still contains a possible dose amount." });
                post.AutoFlagsJson = System.Text.Json.JsonSerializer.Serialize(scan.Flags);
                post.FlagsCount = scan.Flags.Count;
                post.EmergencyFlag = scan.Emergency;
                post.Status = "pending";
                post.PublishedAt = null;
            }
            else if (action == "approve")
            {
                var scan = SafetyScanner.Scan(post.Title, post.Symptoms, post.DurationText, post.Story, post.WhatFailed, post.WhatWorked,
                    string.Join(" ", post.Treatments.Select(t => t.Note)));
                if (scan.HasDose) return Results.UnprocessableEntity(new { message = "Remove the possible dose amounts before approving." });
                post.Status = "published"; post.PublishedAt = DateTimeOffset.UtcNow;
            }
            else if (action == "reject") post.Status = "rejected";
            else if (action == "remove") post.Status = "removed";
            post.UpdatedAt = DateTimeOffset.UtcNow;
            db.ModerationActions.Add(new ModerationAction { ModeratorId = moderatorId.Value, TargetType = "post", TargetId = id, Action = action, Reason = request.Reason.Trim() });
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(moderatorId, $"post_{action}", "post", id, ct);
            if (action is "approve" or "reject")
                await notifications.NotifyAsync(post.UserId, action == "approve" ? "post_approved" : "post_rejected",
                    action == "approve" ? "Your story has been approved and is now visible to the community." : "Your story was not approved. Sign in to review the moderator's note.", id, ct);
            if (action == "remove") await notifications.NotifyAsync(post.UserId, "post_removed", "A moderator removed one of your posts after review.", id, ct);
            return Results.Ok(new { post.Id, post.Status, action });
        }).RequireAuthorization("Moderator");

        app.MapGet("/api/admin/queue/reports", async (PeerCareDbContext db, CancellationToken ct) =>
        {
            var reports = await db.Reports.AsNoTracking().Where(r => r.Status == "open").OrderBy(r => r.CreatedAt).Take(200).ToListAsync(ct);
            var items = new List<object>();
            foreach (var r in reports)
            {
                string? summary = null;
                if (r.TargetType == "post") summary = await db.Posts.Where(p => p.Id == r.TargetId).Select(p => p.Title).FirstOrDefaultAsync(ct);
                else if (r.TargetType == "reply") summary = await db.Replies.Where(p => p.Id == r.TargetId).Select(p => p.Body).FirstOrDefaultAsync(ct);
                else if (r.TargetType == "review") summary = await db.ProviderReviews.Where(p => p.Id == r.TargetId).Select(p => p.ReviewText).FirstOrDefaultAsync(ct);
                else if (r.TargetType == "user") summary = await db.Users.Where(p => p.Id == r.TargetId).Select(p => p.DisplayName).FirstOrDefaultAsync(ct);
                items.Add(new { r.Id, r.TargetType, r.TargetId, r.Reason, r.Detail, r.CreatedAt, targetSummary = summary });
            }
            return Results.Ok(items);
        }).RequireAuthorization("Moderator");

        app.MapGet("/api/admin/reports/{id:guid}", async (Guid id, HttpContext http, PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var moderatorId = ApiSupport.CurrentUserId(http.User);
            var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
            if (report is null) return Results.NotFound();
            object? target = null;
            Guid? anonymousAuthor = null;
            if (report.TargetType == "post")
            {
                var post = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => p.Id == report.TargetId), ct);
                if (post.Count > 0)
                {
                    var item = post[0];
                    target = new { item.Id, item.Kind, condition = item.Condition?.Name, item.Title, item.Story, item.Status, item.IsAnonymous, item.EmergencyFlag };
                    if (item.IsAnonymous) anonymousAuthor = item.UserId;
                }
            }
            else if (report.TargetType == "reply") target = await db.Replies.AsNoTracking().Where(r => r.Id == report.TargetId).Select(r => new { r.Id, r.PostId, r.Body, r.Status }).FirstOrDefaultAsync(ct);
            else if (report.TargetType == "review") target = await db.ProviderReviews.AsNoTracking().Where(r => r.Id == report.TargetId).Select(r => new { r.Id, r.FacilityId, r.ReviewText, r.Status }).FirstOrDefaultAsync(ct);
            else if (report.TargetType == "user") target = await db.Users.AsNoTracking().Where(u => u.Id == report.TargetId).Select(u => new { u.Id, u.DisplayName, u.Status }).FirstOrDefaultAsync(ct);
            string? anonymousAuthorName = null;
            if (anonymousAuthor is not null)
            {
                anonymousAuthorName = await db.Users.Where(u => u.Id == anonymousAuthor).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
                await audit.RecordAsync(moderatorId, "view_anonymous_author", "post", report.TargetId, ct);
            }
            return Results.Ok(new { report.Id, report.TargetType, report.TargetId, report.Reason, report.Detail, report.Status, report.CreatedAt, target, anonymousAuthor = anonymousAuthorName });
        }).RequireAuthorization("Moderator");

        app.MapPost("/api/admin/reports/{id:guid}/action", async (Guid id, AdminReportActionRequest request, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, PeerNotificationService notifications, CancellationToken ct) =>
        {
            var moderatorId = ApiSupport.CurrentUserId(http.User);
            if (moderatorId is null) return Results.Unauthorized();
            var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == id && r.Status == "open", ct);
            if (report is null) return Results.NotFound();
            if (request.Status is not ("actioned" or "dismissed")) return Results.BadRequest(new { message = "Choose actioned or dismissed." });
            report.Status = request.Status; report.HandledBy = moderatorId; report.HandledAt = DateTimeOffset.UtcNow;
            if (request.Status == "actioned")
            {
                if (report.TargetType == "post")
                {
                    var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == report.TargetId, ct);
                    if (post is not null)
                    {
                        post.Status = "removed";
                        post.PublishedAt = null;
                        await notifications.NotifyAsync(post.UserId, "post_removed", "A moderator removed your post after reviewing a community report.", post.Id, ct);
                    }
                }
                else if (report.TargetType == "reply")
                {
                    var reply = await db.Replies.SingleOrDefaultAsync(r => r.Id == report.TargetId, ct);
                    if (reply is not null) reply.Status = "removed";
                }
                else if (report.TargetType == "review")
                {
                    var review = await db.ProviderReviews.SingleOrDefaultAsync(r => r.Id == report.TargetId, ct);
                    if (review is not null) review.Status = "removed";
                }
                else if (report.TargetType == "user")
                {
                    var member = await db.Users.SingleOrDefaultAsync(u => u.Id == report.TargetId, ct);
                    if (member is not null) member.Status = "suspended";
                }
            }
            db.ModerationActions.Add(new ModerationAction { ModeratorId = moderatorId.Value, TargetType = "report", TargetId = id, Action = request.Status, Reason = request.Note.Trim() });
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(moderatorId, $"report_{request.Status}", "report", id, ct);
            return Results.Ok(new { report.Id, report.Status });
        }).RequireAuthorization("Moderator");

        app.MapGet("/api/admin/queue/verifications", async (HttpContext http, PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var adminId = ApiSupport.CurrentUserId(http.User);
            var items = await db.ProfessionalVerifications.AsNoTracking().Include(v => v.User)
                .Where(v => v.Status == "pending").OrderBy(v => v.Id)
                .Select(v => new { v.Id, v.UserId, member = v.User!.DisplayName, v.Profession, v.IssuingBody, v.Status, v.LicenceNumber })
                .ToListAsync(ct);
            await audit.RecordAsync(adminId, "view_professional_licence", "professional_verifications", null, ct);
            return Results.Ok(items);
        }).RequireAuthorization("Admin");

        app.MapPost("/api/admin/verifications/{id:guid}/review", async (Guid id, AdminVerificationRequest request, HttpContext http,
            PeerCareDbContext db, UserManager<PeerUser> users, PeerAuditService audit, PeerNotificationService notifications, CancellationToken ct) =>
        {
            var adminId = ApiSupport.CurrentUserId(http.User);
            if (adminId is null) return Results.Unauthorized();
            if (request.Status is not ("approved" or "rejected")) return Results.BadRequest(new { message = "Choose approved or rejected." });
            var item = await db.ProfessionalVerifications.Include(v => v.User).SingleOrDefaultAsync(v => v.Id == id && v.Status == "pending", ct);
            if (item is null) return Results.NotFound();
            item.Status = request.Status; item.ReviewedBy = adminId; item.ReviewedAt = DateTimeOffset.UtcNow; item.RejectReason = request.Status == "rejected" ? request.RejectReason?.Trim() : null;
            if (request.Status == "approved") await users.AddToRoleAsync(item.User!, "Professional");
            db.ModerationActions.Add(new ModerationAction { ModeratorId = adminId.Value, TargetType = "verification", TargetId = id, Action = request.Status, Reason = item.RejectReason ?? "Licence checked by admin." });
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(adminId, $"professional_verification_{request.Status}", "professional_verification", id, ct);
            await notifications.NotifyAsync(item.UserId, "verification_reviewed", request.Status == "approved" ? "Your professional licence has been verified." : $"Your professional verification was not approved. {item.RejectReason}", null, ct);
            return Results.Ok(new { item.Id, item.Status, item.RejectReason });
        }).RequireAuthorization("Admin");

        app.MapGet("/api/admin/queue/facilities", async (PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Facilities.AsNoTracking().Where(f => f.Status == "unverified").OrderBy(f => f.Name)
                .Select(f => new { f.Id, f.Name, f.Type, f.Address, f.State, f.City }).ToListAsync(ct)))
            .RequireAuthorization("Moderator");

        app.MapGet("/api/admin/queue/facility-issues", async (PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.FacilityIssues.AsNoTracking().Include(i => i.Facility).Where(i => i.Status == "open").OrderBy(i => i.CreatedAt)
                .Select(i => new { i.Id, i.FacilityId, facility = i.Facility!.Name, i.Detail, i.CreatedAt }).ToListAsync(ct)))
            .RequireAuthorization("Moderator");

        app.MapPost("/api/admin/facility-issues/{id:guid}/close", async (Guid id, HttpContext http, PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var moderatorId = ApiSupport.CurrentUserId(http.User);
            if (moderatorId is null) return Results.Unauthorized();
            var issue = await db.FacilityIssues.SingleOrDefaultAsync(i => i.Id == id && i.Status == "open", ct);
            if (issue is null) return Results.NotFound();
            issue.Status = "handled"; issue.HandledBy = moderatorId;
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(moderatorId, "facility_issue_handled", "facility_issue", id, ct);
            return Results.Ok(new { issue.Id, issue.Status });
        }).RequireAuthorization("Moderator");

        app.MapPost("/api/admin/practitioners/{id:guid}/verify", async (Guid id, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var moderatorId = ApiSupport.CurrentUserId(http.User);
            if (moderatorId is null) return Results.Unauthorized();
            var practitioner = await db.Practitioners.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (practitioner is null) return Results.NotFound();
            var reports = await db.PostFacilities.Where(pf => pf.PractitionerId == id && pf.Post!.Status == "published")
                .Select(pf => pf.Post!.UserId).Distinct().CountAsync(ct);
            if (reports < 5) return Results.Conflict(new { message = "A clinician name stays hidden until five independent reports are available.", independentReports = reports });
            practitioner.Status = "visible"; practitioner.ReportCount = reports;
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(moderatorId, "practitioner_verified", "practitioner", id, ct);
            return Results.Ok(new { practitioner.Id, status = practitioner.Status, independentReports = reports });
        }).RequireAuthorization("Moderator");

        app.MapGet("/api/admin/users", async (string? q, PeerCareDbContext db, UserManager<PeerUser> users, CancellationToken ct) =>
        {
            var query = db.Users.AsNoTracking().Where(u => u.Status != "deleted");
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(u => u.DisplayName.ToLower().Contains(q.Trim().ToLower()));
            var selected = await query.OrderByDescending(u => u.CreatedAt).Take(100).ToListAsync(ct);
            var result = new List<object>();
            foreach (var user in selected) result.Add(new { user.Id, user.DisplayName, user.Status, user.State, user.City, roles = await users.GetRolesAsync(user), user.CreatedAt });
            return Results.Ok(result);
        }).RequireAuthorization("Moderator");

        app.MapPost("/api/admin/users/{id:guid}/suspension", async (Guid id, AdminSuspendRequest request, HttpContext http,
            PeerCareDbContext db, PeerAuditService audit, CancellationToken ct) =>
        {
            var actor = ApiSupport.CurrentUserId(http.User);
            if (actor is null) return Results.Unauthorized();
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.NotFound();
            if (user.Id == actor) return Results.BadRequest(new { message = "You cannot suspend your own account." });
            user.Status = request.Suspended ? "suspended" : "active";
            await db.SaveChangesAsync(ct);
            db.ModerationActions.Add(new ModerationAction { ModeratorId = actor.Value, TargetType = "user", TargetId = id, Action = request.Suspended ? "suspend" : "reinstate", Reason = request.Reason.Trim() });
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(actor, request.Suspended ? "user_suspended" : "user_reinstated", "user", id, ct);
            return Results.Ok(new { user.Id, user.Status });
        }).RequireAuthorization("Moderator");

        app.MapGet("/api/admin/users/{id:guid}/contact", async (Guid id, HttpContext http, PeerCareDbContext db,
            PeerAuditService audit, CancellationToken ct) =>
        {
            var adminId = ApiSupport.CurrentUserId(http.User);
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.NotFound();
            await audit.RecordAsync(adminId, "view_user_contact_details", "user", id, ct);
            return Results.Ok(new { user.Id, email = user.Email, phone = user.PhoneNumber });
        }).RequireAuthorization("Admin");

        app.MapGet("/api/admin/audit", async (PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(500)
                .Select(a => new { a.Id, a.ActorId, a.Action, a.SubjectType, a.SubjectId, a.IpHash, a.CreatedAt }).ToListAsync(ct)))
            .RequireAuthorization("Admin");
    }
}
