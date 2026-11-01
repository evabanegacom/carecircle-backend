using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeerCare.Data;
using PeerCare.Domain;

namespace PeerCare.Services;

public sealed class PeerNotificationService(
    PeerCareDbContext db,
    ContactDelivery delivery,
    ILogger<PeerNotificationService> logger)
{
    public async Task NotifyAsync(Guid userId, string kind, string message, Guid? postId = null, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.Status == "active", cancellationToken);
        if (user is null) return;
        var payload = JsonSerializer.Serialize(new { message, postId });
        if (user.InAppNotifications)
        {
            db.Notifications.Add(new PeerNotification { UserId = userId, Kind = kind, PayloadJson = payload });
            await db.SaveChangesAsync(cancellationToken);
        }
        if (user.EmailNotifications && user.EmailConfirmed && !string.IsNullOrWhiteSpace(user.Email))
        {
            try { await delivery.SendNotificationAsync(user.Email, "A HealthStories update", message, cancellationToken); }
            catch (Exception ex) { logger.LogError(ex, "Could not deliver a PeerCare notification."); }
        }
    }

    public async Task NotifyCommunityMembersAsync(Guid conditionId, Guid authorId, Guid postId, CancellationToken cancellationToken = default)
    {
        var communities = await db.Communities.AsNoTracking()
            .Where(c => c.Status == "active" && c.ConditionId == conditionId)
            .Select(c => new { c.Id, c.Name, c.Slug }).ToListAsync(cancellationToken);
        if (communities.Count == 0) return;

        var communityIds = communities.Select(c => c.Id).ToArray();
        var memberIds = await db.CommunityMemberships.AsNoTracking()
            .Where(m => communityIds.Contains(m.CommunityId) && m.Status == "joined" && m.UserId != authorId)
            .Select(m => m.UserId).Distinct().ToArrayAsync(cancellationToken);
        if (memberIds.Length == 0) return;

        var recipients = await db.Users.AsNoTracking()
            .Where(u => memberIds.Contains(u.Id) && u.Status == "active" && (u.InAppNotifications || u.EmailNotifications))
            .Select(u => new { u.Id, u.Email, u.EmailConfirmed, u.InAppNotifications, u.EmailNotifications })
            .ToListAsync(cancellationToken);
        var community = communities[0];
        var message = $"A new question is available in {community.Name}. Open the community to read it and share your experience.";
        var payload = JsonSerializer.Serialize(new { message, postId, communitySlug = community.Slug });

        foreach (var recipient in recipients.Where(r => r.InAppNotifications))
            db.Notifications.Add(new PeerNotification { UserId = recipient.Id, Kind = "community_question", PayloadJson = payload });
        await db.SaveChangesAsync(cancellationToken);

        foreach (var recipient in recipients.Where(r => r.EmailNotifications && r.EmailConfirmed && !string.IsNullOrWhiteSpace(r.Email)))
        {
            try { await delivery.SendNotificationAsync(recipient.Email!, "A HealthStories update", "A new question is available in one of your communities. Sign in to read it and share your experience.", cancellationToken); }
            catch (Exception ex) { logger.LogError(ex, "Could not deliver a community question notification."); }
        }
    }
}
