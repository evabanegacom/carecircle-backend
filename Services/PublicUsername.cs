using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PeerCare.Data;

namespace PeerCare.Services;

public static class PublicUsername
{
    public const string DuplicateMessage = "That username is already in use. Choose another.";

    public static string Normalize(string username) => username.Trim().ToUpperInvariant();

    public static async Task<bool> IsTakenAsync(PeerCareDbContext db, Guid currentUserId, string normalizedUsername, CancellationToken ct)
    {
        if (await db.Users.AsNoTracking().AnyAsync(user => user.Id != currentUserId
            && user.NormalizedDisplayName == normalizedUsername, ct))
            return true;

        var legacyNames = await db.Users.AsNoTracking().Where(user => user.Id != currentUserId
                && user.NormalizedDisplayName == null)
            .Select(user => user.DisplayName).ToListAsync(ct);
        return legacyNames.Any(name => string.Equals(Normalize(name), normalizedUsername, StringComparison.Ordinal));
    }

    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (DbException? current = exception.InnerException as DbException; current is not null; current = current.InnerException as DbException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && postgres.ConstraintName == "IX_users_NormalizedDisplayName")
                return true;
            if (current is SqliteException sqlite && sqlite.SqliteErrorCode == 19
                && sqlite.Message.Contains("NormalizedDisplayName", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
