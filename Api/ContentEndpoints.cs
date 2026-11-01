using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PeerCare.Contracts;
using PeerCare.Data;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Api;

public static class ContentEndpoints
{
    public static void MapPeerCareContent(this WebApplication app)
    {
        app.MapGet("/api/conditions", async (PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Conditions.AsNoTracking().Where(c => c.Active).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.Slug, c.Category, c.ParentId }).ToListAsync(ct)));
        app.MapGet("/api/topics", async (PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Conditions.AsNoTracking().Where(c => c.Active).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.Slug, c.Category, c.ParentId }).ToListAsync(ct)));
        app.MapGet("/api/treatments", async (string? q, PeerCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Treatments.AsNoTracking().Where(t => t.Approved && (string.IsNullOrWhiteSpace(q) || t.Name.ToLower().Contains(q.ToLower()))).OrderBy(t => t.Name).Take(50).Select(t => new { t.Id, t.Name, t.Kind }).ToListAsync(ct)));

        app.MapGet("/api/posts", async (string? q, Guid? conditionId, string? condition, string? ageGroup, string? treatment,
            string? outcome, string? hospital, string? state, string? city, string? kind, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
            HttpContext http, PeerCareDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var query = db.Posts.AsNoTracking().Where(p => p.Status == "published");
            var viewerId = ApiSupport.CurrentUserId(http.User);
            if (viewerId is not null)
            {
                var hiddenAuthors = await db.UserBlocks.Where(b => (b.Kind == "block" && (b.BlockerId == viewerId || b.BlockedId == viewerId)) || (b.Kind == "mute" && b.BlockerId == viewerId))
                    .Select(b => b.BlockerId == viewerId ? b.BlockedId : b.BlockerId).Distinct().ToArrayAsync(ct);
                query = query.Where(p => !hiddenAuthors.Contains(p.UserId));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.Trim()[..Math.Min(q.Trim().Length, 120)];
                if (db.Database.IsNpgsql())
                {
                    query = query.Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", search)) || p.Replies.Any(r => r.Status == "published" && r.Body.ToLower().Contains(search.ToLower())) || p.Condition!.Name.ToLower().Contains(search.ToLower()));
                }
                else
                {
                    var lowered = search.ToLower();
                    query = query.Where(p => p.Title.ToLower().Contains(lowered) || p.Symptoms.ToLower().Contains(lowered) || p.Story.ToLower().Contains(lowered) || p.Condition!.Name.ToLower().Contains(lowered) || p.Replies.Any(r => r.Status == "published" && r.Body.ToLower().Contains(lowered)));
                }
            }
            if (conditionId is not null) query = query.Where(p => p.ConditionId == conditionId);
            if (!string.IsNullOrWhiteSpace(condition)) query = query.Where(p => p.Condition != null && p.Condition.Name.ToLower() == condition.Trim().ToLower());
            if (!string.IsNullOrWhiteSpace(ageGroup)) query = query.Where(p => p.AgeGroup == ageGroup);
            if (!string.IsNullOrWhiteSpace(kind) && kind != "all") query = query.Where(p => p.Kind == kind);
            if (from is not null) query = query.Where(p => p.PublishedAt >= from);
            if (to is not null) query = query.Where(p => p.PublishedAt < to.Value.AddDays(1));
            if (!string.IsNullOrWhiteSpace(state)) query = query.Where(p => p.State.ToLower() == state.Trim().ToLower());
            if (!string.IsNullOrWhiteSpace(city)) query = query.Where(p => p.City.ToLower() == city.Trim().ToLower());
            if (!string.IsNullOrWhiteSpace(hospital)) query = query.Where(p => p.Facilities.Any(f => f.Facility != null && f.Facility.Name.ToLower().Contains(hospital.Trim().ToLower())));
            if (!string.IsNullOrWhiteSpace(treatment)) query = query.Where(p => p.Treatments.Any(t => t.Treatment != null && t.Treatment.Name.ToLower().Contains(treatment.Trim().ToLower())));
            if (!string.IsNullOrWhiteSpace(outcome))
            {
                var value = NormalizeOutcome(outcome);
                query = query.Where(p => p.Outcome == value || p.Treatments.Any(t => t.Outcome == value));
            }
            var total = await query.CountAsync(ct);
            var currentPage = Math.Max(1, page ?? 1);
            var currentPageSize = Math.Clamp(pageSize ?? 12, 1, 30);
            var selected = query.OrderBy(p => p.Kind == "resolution" ? 0 : p.Kind == "experience" ? 1 : 2)
                .ThenByDescending(p => p.PublishedAt).Skip((currentPage - 1) * currentPageSize).Take(currentPageSize);
            var posts = await ApiSupport.LoadPostsAsync(selected, ct);
            var items = new List<object>();
            foreach (var post in posts) items.Add(await ApiSupport.PostCardAsync(db, post, viewerId, ct: ct));
            return Results.Ok(new { items, total, page = currentPage, pageSize = currentPageSize });
        }).RequireRateLimiting("search");

        app.MapGet("/api/posts/{id:guid}", async (Guid id, HttpContext http, PeerCareDbContext db, UserManager<PeerUser> users, CancellationToken ct) =>
        {
            var viewerId = ApiSupport.CurrentUserId(http.User);
            var post = await ApiSupport.LoadPostsAsync(db.Posts.Where(p => p.Id == id && (p.Status == "published" || (viewerId != null && p.UserId == viewerId))), ct);
            if (post.Count == 0) return Results.NotFound();
            var item = post[0];
            var card = await ApiSupport.PostCardAsync(db, item, viewerId, includePrivateStatus: viewerId == item.UserId, ct: ct);
            var replies = await db.Replies.AsNoTracking().Include(r => r.User)
                .Where(r => r.PostId == id && r.Status == "published" && (viewerId == null || !db.UserBlocks.Any(b => (b.Kind == "block" && ((b.BlockerId == viewerId && b.BlockedId == r.UserId) || (b.BlockerId == r.UserId && b.BlockedId == viewerId))) || (b.Kind == "mute" && b.BlockerId == viewerId && b.BlockedId == r.UserId))))
                .OrderByDescending(r => r.IsProfessionalNote).ThenBy(r => r.CreatedAt).ToListAsync(ct);
            var replyDtos = new List<object>();
            foreach (var reply in replies)
            {
                var roles = await users.GetRolesAsync(reply.User!);
                var profession = reply.IsProfessionalNote ? await db.ProfessionalVerifications.AsNoTracking()
                    .Where(v => v.UserId == reply.UserId && v.Status == "approved").Select(v => v.Profession).FirstOrDefaultAsync(ct) : null;
                replyDtos.Add(new
                {
                    reply.Id, reply.PostId, reply.ParentId, reply.Body, reply.IsProfessionalNote,
                    author = reply.IsProfessionalNote ? reply.User!.DisplayName : reply.User!.DisplayName,
                    verifiedProfessional = reply.IsProfessionalNote && roles.Contains("Professional"), profession,
                    reply.CreatedAt,
                    thanksCount = await db.Reactions.CountAsync(r => r.TargetType == "reply" && r.TargetId == reply.Id && r.Kind == "thank_you", ct)
                });
            }
            return Results.Ok(new { post = card, replies = replyDtos, disclaimer = "This is one family's experience, not medical advice. Talk with a qualified health professional about your care." });
        });

        app.MapPost("/api/posts", async (CreatePostRequest request, HttpContext http, PeerCareDbContext db,
            IConfiguration config, PeerNotificationService notifications, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the health data consent before sharing." });
            var kind = request.Kind.Trim().ToLowerInvariant();
            if (kind is not ("resolution" or "experience" or "question")) return Results.BadRequest(new { message = "Choose a story, an in-progress experience, or a question." });
            var diagnosisStatus = (request.DiagnosisStatus ?? "not_stated").Trim().ToLowerInvariant();
            if (diagnosisStatus is not ("not_stated" or "suspected" or "reported_confirmed" or "not_applicable")) return Results.BadRequest(new { message = "Choose a valid diagnosis context." });
            var condition = await db.Conditions.SingleOrDefaultAsync(c => c.Id == request.ConditionId && c.Active, ct);
            if (condition is null) return Results.BadRequest(new { message = "Choose a health topic from the list." });
            var title = request.Title.Trim();
            var story = request.Story.Trim();
            if (title.Length is < 4 or > 180 || story.Length is < 10 or > 5000)
                return Results.BadRequest(new { message = "Add a short title and a story of at least 10 characters." });
            if (string.IsNullOrWhiteSpace(request.AgeGroup) || request.AgeGroup.Length > 40)
                return Results.BadRequest(new { message = "Choose an age group." });
            if (kind == "question" && string.IsNullOrWhiteSpace(request.HelpNeeded))
                return Results.BadRequest(new { message = "Choose what kind of help you need." });
            if (kind == "resolution" && (string.IsNullOrWhiteSpace(request.WhatFailed) || string.IsNullOrWhiteSpace(request.WhatWorked) || string.IsNullOrWhiteSpace(request.TimeToResolve)))
                return Results.BadRequest(new { message = "Resolution stories need what did not work, what finally helped, and how long it took." });
            var treatments = request.Treatments ?? [];
            if (treatments.Count > 12) return Results.BadRequest(new { message = "Add up to 12 treatments." });
            foreach (var treatment in treatments)
            {
                if (string.IsNullOrWhiteSpace(treatment.Name) || treatment.Name.Length > 160 || treatment.Kind.Length > 32 || treatment.Note?.Length > 500)
                    return Results.BadRequest(new { message = "Check the treatment details and try again." });
                if (treatment.Outcome is not ("helped" or "no_change" or "worse" or "unsure"))
                    return Results.BadRequest(new { message = "Choose an outcome for each treatment." });
            }
            var scan = SafetyScanner.Scan(title, request.Symptoms, request.DurationText, story, request.HelpNeeded, request.WhatFailed, request.WhatWorked,
                string.Join(" ", treatments.Select(t => $"{t.Name} {t.Note}")));
            if (scan.HasDose) return Results.UnprocessableEntity(new { message = "Please remove medication amounts and dose schedules before sharing.", flags = scan.Flags });
            var existingCount = await db.Posts.CountAsync(p => p.UserId == user.Id, ct);
            var isFirstTwo = existingCount < 2;
            var facilities = request.Facilities ?? [];
            var newFacility = false;
            var post = new PeerPost
            {
                UserId = user.Id, Kind = kind, ConditionId = condition.Id, AgeGroup = request.AgeGroup.Trim(), About = request.About,
                Title = title, Symptoms = request.Symptoms?.Trim() ?? "", DurationText = request.DurationText?.Trim() ?? "", Story = story,
                Outcome = NormalizeOutcome(request.Outcome), DiagnosisStatus = diagnosisStatus, HelpNeeded = request.HelpNeeded?.Trim(), WhatFailed = request.WhatFailed?.Trim(),
                WhatWorked = request.WhatWorked?.Trim(), TimeToResolve = request.TimeToResolve?.Trim(), IsAnonymous = request.IsAnonymous,
                State = string.IsNullOrWhiteSpace(request.State) ? user.State ?? "Lagos" : request.State.Trim(),
                City = string.IsNullOrWhiteSpace(request.City) ? user.City ?? "Lagos" : request.City.Trim(),
                EmergencyFlag = scan.Emergency, FlagsCount = scan.Flags.Count, AutoFlagsJson = System.Text.Json.JsonSerializer.Serialize(scan.Flags),
                Status = isFirstTwo || scan.Flags.Count != 0 ? "pending" : "published",
                PublishedAt = isFirstTwo || scan.Flags.Count != 0 ? null : DateTimeOffset.UtcNow
            };
            foreach (var item in treatments)
            {
                var name = item.Name.Trim();
                var treatment = await db.Treatments.SingleOrDefaultAsync(t => t.Name.ToLower() == name.ToLower(), ct);
                if (treatment is null)
                {
                    treatment = new Treatment { Name = name, Kind = item.Kind.Trim().ToLowerInvariant(), Approved = false };
                    db.Treatments.Add(treatment);
                }
                post.Treatments.Add(new PostTreatment { Treatment = treatment, Outcome = item.Outcome, Note = item.Note?.Trim() });
            }
            foreach (var item in facilities.Take(5))
            {
                if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 180) continue;
                var city = string.IsNullOrWhiteSpace(item.City) ? post.City : item.City.Trim();
                var facility = await db.Facilities.FirstOrDefaultAsync(f => f.Name.ToLower() == item.Name.Trim().ToLower() && f.City.ToLower() == city.ToLower(), ct);
                if (facility is null)
                {
                    facility = new Facility { Name = item.Name.Trim(), City = city, State = string.IsNullOrWhiteSpace(item.State) ? post.State : item.State.Trim(), CreatedBy = user.Id };
                    db.Facilities.Add(facility);
                    newFacility = true;
                }
                var relation = new PostFacility { Post = post, Facility = facility, Result = NormalizeFacilityResult(item.Result) };
                if (!string.IsNullOrWhiteSpace(item.Practitioner) && item.Practitioner.Length <= 140)
                {
                    var practitionerName = item.Practitioner.Trim();
                    var practitioner = await db.Practitioners.FirstOrDefaultAsync(p => p.FacilityId == facility.Id && p.Name.ToLower() == practitionerName.ToLower(), ct);
                    if (practitioner is null)
                    {
                        practitioner = new Practitioner { Facility = facility, Name = practitionerName, Status = "hidden" };
                        db.Practitioners.Add(practitioner);
                    }
                    relation.Practitioner = practitioner;
                    practitioner.ReportCount++;
                }
                post.Facilities.Add(relation);
            }
            if (newFacility && post.Status == "published") { post.Status = "pending"; post.PublishedAt = null; }
            db.Posts.Add(post);
            await db.SaveChangesAsync(ct);
            if (post.Status == "published" && post.Kind == "question")
                await notifications.NotifyCommunityMembersAsync(post.ConditionId, user.Id, post.Id, ct);
            if (post.Status == "published" && post.EmergencyFlag)
                await notifications.NotifyAsync(user.Id, "post_published", "Your story is published. If it describes an emergency, go to the nearest hospital.", post.Id, ct);
            return Results.Created($"/api/posts/{post.Id}", new
            {
                post = await ApiSupport.PostCardAsync(db, (await ApiSupport.LoadPostsAsync(db.Posts.Where(p => p.Id == post.Id), ct))[0], user.Id, includePrivateStatus: true, ct: ct),
                message = post.Status == "pending" ? "Your story is waiting for moderator review." : "Your story has been shared.",
                flags = scan.Flags,
                emergency = scan.Emergency
            });
        }).RequireAuthorization().RequireRateLimiting("posting");

        app.MapPut("/api/posts/{id:guid}", async (Guid id, EditPostRequest request, HttpContext http, PeerCareDbContext db,
            IConfiguration config, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the health data consent before editing stories." });
            var diagnosisStatus = (request.DiagnosisStatus ?? "not_stated").Trim().ToLowerInvariant();
            if (diagnosisStatus is not ("not_stated" or "suspected" or "reported_confirmed" or "not_applicable")) return Results.BadRequest(new { message = "Choose a valid diagnosis context." });
            var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == id && p.UserId == user.Id, ct);
            if (post is null) return Results.NotFound();
            if (post.Status == "removed") return Results.Conflict(new { message = "This story has been removed." });
            var scan = SafetyScanner.Scan(request.Title, request.Symptoms, request.DurationText, request.Story, request.WhatFailed, request.WhatWorked);
            if (scan.HasDose) return Results.UnprocessableEntity(new { message = "Please remove medication amounts and dose schedules before saving." });
            post.Title = request.Title.Trim(); post.Symptoms = request.Symptoms.Trim(); post.DurationText = request.DurationText.Trim();
            post.Story = request.Story.Trim(); post.Outcome = NormalizeOutcome(request.Outcome); post.DiagnosisStatus = diagnosisStatus; post.WhatFailed = request.WhatFailed; post.WhatWorked = request.WhatWorked;
            post.AutoFlagsJson = System.Text.Json.JsonSerializer.Serialize(scan.Flags); post.FlagsCount = scan.Flags.Count; post.EmergencyFlag = scan.Emergency;
            post.Status = "pending"; post.PublishedAt = null; post.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { saved = true, status = post.Status, flags = scan.Flags });
        }).RequireAuthorization();

        app.MapDelete("/api/posts/{id:guid}", async (Guid id, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);
            if (post is null) return Results.NotFound();
            post.Status = "removed";
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { removed = true });
        }).RequireAuthorization();

        app.MapPost("/api/posts/{id:guid}/replies", async (Guid id, ReplyRequest request, HttpContext http, PeerCareDbContext db,
            UserManager<PeerUser> users, IConfiguration config, PeerNotificationService notifications, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the health data consent before replying." });
            var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == id && p.Status == "published", ct);
            if (post is null) return Results.NotFound();
            var body = request.Body.Trim();
            if (body.Length < 2 || body.Length > 4000) return Results.BadRequest(new { message = "Write a reply between 2 and 4,000 characters." });
            var scan = SafetyScanner.Scan(body);
            if (scan.HasDose) return Results.UnprocessableEntity(new { message = "Please remove medication amounts and dose schedules before replying." });
            var isProfessional = request.ProfessionalNote;
            if (isProfessional && (!await users.IsInRoleAsync(user, "Professional") || !await ApiSupport.IsVerifiedProfessionalAsync(db, user, ct)))
                return Results.Forbid();
            if (request.ParentId is not null && !await db.Replies.AnyAsync(r => r.Id == request.ParentId && r.PostId == id && r.Status == "published", ct))
                return Results.BadRequest(new { message = "That reply is no longer available." });
            var reply = new PeerReply
            {
                PostId = id, UserId = user.Id, ParentId = request.ParentId, Body = body,
                IsProfessionalNote = isProfessional, Status = scan.Flags.Count > 0 ? "pending" : "published"
            };
            db.Replies.Add(reply);
            await db.SaveChangesAsync(ct);
            if (reply.Status == "published" && post.UserId != user.Id)
                await notifications.NotifyAsync(post.UserId, isProfessional ? "professional_note" : "reply", isProfessional ? "A verified professional added a note to your post." : "Someone replied to your post.", post.Id, ct);
            return Results.Created($"/api/posts/{id}", new { reply.Id, reply.PostId, reply.Body, reply.IsProfessionalNote, status = reply.Status, flags = scan.Flags });
        }).RequireAuthorization().RequireRateLimiting("posting");

        app.MapDelete("/api/replies/{id:guid}", async (Guid id, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            var reply = await db.Replies.SingleOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
            if (reply is null) return Results.NotFound();
            reply.Status = "removed";
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { removed = true });
        }).RequireAuthorization();

        app.MapPost("/api/posts/{id:guid}/reactions", async (Guid id, ReactionRequest request, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            if (request.Kind is not ("helped_me_too" or "thank_you")) return Results.BadRequest(new { message = "Choose a reaction." });
            if (!await db.Posts.AnyAsync(p => p.Id == id && p.Status == "published", ct)) return Results.NotFound();
            var existing = await db.Reactions.SingleOrDefaultAsync(r => r.UserId == userId && r.TargetType == "post" && r.TargetId == id && r.Kind == request.Kind, ct);
            if (existing is null) db.Reactions.Add(new PeerReaction { UserId = userId.Value, TargetType = "post", TargetId = id, Kind = request.Kind });
            else db.Reactions.Remove(existing);
            await db.SaveChangesAsync(ct);
            var count = await db.Reactions.CountAsync(r => r.TargetType == "post" && r.TargetId == id && r.Kind == request.Kind, ct);
            return Results.Ok(new { kind = request.Kind, count, active = existing is null });
        }).RequireAuthorization();

        app.MapPost("/api/replies/{id:guid}/reactions", async (Guid id, ReactionRequest request, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            if (request.Kind is not ("helped_me_too" or "thank_you")) return Results.BadRequest(new { message = "Choose a reaction." });
            if (!await db.Replies.AnyAsync(r => r.Id == id && r.Status == "published", ct)) return Results.NotFound();
            var existing = await db.Reactions.SingleOrDefaultAsync(r => r.UserId == userId && r.TargetType == "reply" && r.TargetId == id && r.Kind == request.Kind, ct);
            if (existing is null) db.Reactions.Add(new PeerReaction { UserId = userId.Value, TargetType = "reply", TargetId = id, Kind = request.Kind });
            else db.Reactions.Remove(existing);
            await db.SaveChangesAsync(ct);
            var count = await db.Reactions.CountAsync(r => r.TargetType == "reply" && r.TargetId == id && r.Kind == request.Kind, ct);
            return Results.Ok(new { kind = request.Kind, count, active = existing is null });
        }).RequireAuthorization();

        app.MapPost("/api/posts/{id:guid}/save", async (Guid id, HttpContext http, PeerCareDbContext db, CancellationToken ct) =>
        {
            var userId = ApiSupport.CurrentUserId(http.User);
            if (userId is null) return Results.Unauthorized();
            if (!await db.Posts.AnyAsync(p => p.Id == id && p.Status == "published", ct)) return Results.NotFound();
            var existing = await db.Saves.FindAsync([userId.Value, id], ct);
            if (existing is null) db.Saves.Add(new PeerSave { UserId = userId.Value, PostId = id }); else db.Saves.Remove(existing);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { saved = existing is null });
        }).RequireAuthorization();

        app.MapPost("/api/reports", async (ReportRequest request, HttpContext http, PeerCareDbContext db, IConfiguration config,
            PeerNotificationService notifications, CancellationToken ct) =>
        {
            var user = await ApiSupport.CurrentUserAsync(http.User, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!ApiSupport.HasConsent(user, config)) return Results.Conflict(new { message = "Accept the health data consent before reporting content." });
            var target = request.TargetType.Trim().ToLowerInvariant();
            if (target is not ("post" or "reply" or "review" or "user")) return Results.BadRequest(new { message = "Choose a post, reply, review, or profile to report." });
            var exists = target == "post"
                ? await db.Posts.AnyAsync(p => p.Id == request.TargetId && p.Status == "published", ct)
                : target == "reply" ? await db.Replies.AnyAsync(r => r.Id == request.TargetId && r.Status == "published", ct)
                : target == "review" ? await db.ProviderReviews.AnyAsync(r => r.Id == request.TargetId && r.Status == "approved", ct)
                : await db.Users.AnyAsync(u => u.Id == request.TargetId && u.Status == "active" && u.Id != user.Id, ct);
            if (!exists) return Results.NotFound();
            var reason = request.Reason.Trim().ToLowerInvariant();
            if (reason is not ("unsafe_advice" or "privacy" or "false_claim" or "spam" or "harassment" or "other"))
                return Results.BadRequest(new { message = "Choose a report reason." });
            var duplicate = await db.Reports.AnyAsync(r => r.ReporterId == user.Id && r.TargetType == target && r.TargetId == request.TargetId && r.Status == "open", ct);
            if (duplicate) return Results.Conflict(new { message = "You have already reported this item." });
            var report = new PeerReport { ReporterId = user.Id, TargetType = target, TargetId = request.TargetId, Reason = reason, Detail = request.Detail?.Trim() is { Length: > 0 } detail ? detail[..Math.Min(detail.Length, 1200)] : null };
            db.Reports.Add(report);
            await db.SaveChangesAsync(ct);
            var reportCount = await db.Reports.CountAsync(r => r.TargetType == target && r.TargetId == request.TargetId && r.Status == "open", ct);
            if (reportCount >= 3)
            {
                if (target == "post")
                {
                    var post = await db.Posts.SingleAsync(p => p.Id == request.TargetId, ct);
                    post.Status = "pending"; post.PublishedAt = null; post.FlagsCount++;
                    await notifications.NotifyAsync(post.UserId, "post_review", "Your post is temporarily hidden while moderators review reports.", post.Id, ct);
                }
                else
                {
                    var reply = await db.Replies.SingleAsync(r => r.Id == request.TargetId, ct);
                    reply.Status = "hidden";
                }
                await db.SaveChangesAsync(ct);
            }
            return Results.Accepted(value: new { received = true, temporarilyHidden = reportCount >= 3 });
        }).RequireAuthorization().RequireRateLimiting("posting");
    }

    private static string NormalizeOutcome(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "helped" or "resolved" => "helped",
        "improving" or "partly improved" => "improving",
        "no_change" or "no change" => "no_change",
        "worse" or "made worse" => "worse",
        _ => "unknown"
    };

    private static string NormalizeFacilityResult(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "helped" or "helped_us" => "helped_us",
        "no_change" or "no change" => "no_change",
        "worse" or "made_worse" or "made worse" => "made_worse",
        _ => "helped_us"
    };
}
