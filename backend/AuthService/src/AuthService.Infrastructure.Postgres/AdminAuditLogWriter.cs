using AuthService.Core.Database;
using AuthService.Domain.AdminAuditLog;

namespace AuthService.Infrastructure.Postgres;

public sealed class AdminAuditLogWriter : IAdminAuditLogWriter
{
    private readonly AuthDbContext _db;
    private readonly TimeProvider _clock;

    public AdminAuditLogWriter(AuthDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task WriteAsync(
        Guid adminId,
        Guid? targetUserId,
        string action,
        string method,
        string path,
        string? payloadJson,
        string result,
        string? errorMessage,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        AdminAuditLogEntry entry = AdminAuditLogEntry.Create(
            adminId,
            targetUserId,
            action,
            method,
            path,
            payloadJson,
            result,
            errorMessage,
            ipAddress,
            userAgent,
            _clock.GetUtcNow().UtcDateTime);

        await _db.AdminAuditLogs.AddAsync(entry, ct);
        await _db.SaveChangesAsync(ct);
    }
}
