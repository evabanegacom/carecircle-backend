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
}
