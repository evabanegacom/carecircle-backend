using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeerCare.Data;
using PeerCare.Domain;

namespace PeerCare.Services;

public sealed class AccountPurgeService(IServiceScopeFactory scopes, ILogger<AccountPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredAccounts(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "PeerCare account deletion cleanup did not complete.");
            }
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task PurgeExpiredAccounts(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PeerCareDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PeerUser>>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var expired = await db.Users.Where(u => u.DeleteRequestedAt != null && u.DeleteRequestedAt < cutoff).ToListAsync(cancellationToken);
        foreach (var user in expired)
        {
            db.Reactions.RemoveRange(await db.Reactions.Where(r => r.UserId == user.Id).ToListAsync(cancellationToken));
            db.Reports.RemoveRange(await db.Reports.Where(r => r.ReporterId == user.Id).ToListAsync(cancellationToken));
            db.Consents.RemoveRange(await db.Consents.Where(c => c.UserId == user.Id).ToListAsync(cancellationToken));
            db.UserConditions.RemoveRange(await db.UserConditions.Where(c => c.UserId == user.Id).ToListAsync(cancellationToken));
            db.Notifications.RemoveRange(await db.Notifications.Where(n => n.UserId == user.Id).ToListAsync(cancellationToken));
            db.AuditLogs.RemoveRange(await db.AuditLogs.Where(a => a.ActorId == user.Id).ToListAsync(cancellationToken));
            db.ModerationActions.RemoveRange(await db.ModerationActions.Where(a => a.ModeratorId == user.Id).ToListAsync(cancellationToken));
            db.FacilityIssues.RemoveRange(await db.FacilityIssues.Where(i => i.ReporterId == user.Id).ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
            var result = await users.DeleteAsync(user);
            if (!result.Succeeded) logger.LogError("Could not permanently remove a PeerCare account after its deletion window.");
        }
    }
}
