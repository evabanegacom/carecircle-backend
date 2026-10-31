using System.Net;
using Microsoft.AspNetCore.Http;
using PeerCare.Data;
using PeerCare.Domain;

namespace PeerCare.Services;

public sealed class PeerAuditService(PeerCareDbContext db, IHttpContextAccessor http, ContactCrypto crypto)
{
    public async Task RecordAsync(Guid? actorId, string action, string subjectType, Guid? subjectId, CancellationToken cancellationToken = default)
    {
        var address = http.HttpContext?.Connection.RemoteIpAddress;
        var normalized = address is null ? null : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
        db.AuditLogs.Add(new PeerAuditLog
        {
            ActorId = actorId,
            Action = action,
            SubjectType = subjectType,
            SubjectId = subjectId,
            IpHash = normalized is null ? null : crypto.BlindIndex("ip:" + normalized)
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
