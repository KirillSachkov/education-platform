namespace AuthService.Core.Database;

public interface IAdminAuditLogWriter
{
    Task WriteAsync(
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
        CancellationToken ct);
}
