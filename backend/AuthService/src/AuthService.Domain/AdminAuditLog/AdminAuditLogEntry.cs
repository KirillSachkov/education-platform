namespace AuthService.Domain.AdminAuditLog;

public sealed class AdminAuditLogEntry
{
    public Guid Id { get; private set; }
    public Guid AdminId { get; private set; }
    public Guid? TargetUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string Method { get; private set; } = null!;
    public string Path { get; private set; } = null!;
    public string? PayloadJson { get; private set; }
    public string Result { get; private set; } = null!;
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    private AdminAuditLogEntry()
    {
    }

    public static AdminAuditLogEntry Create(
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
        DateTime createdAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            AdminId = adminId,
            TargetUserId = targetUserId,
            Action = action,
            Method = method,
            Path = path,
            PayloadJson = payloadJson,
            Result = result,
            ErrorMessage = errorMessage,
            CreatedAt = createdAt,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };
}

public static class AdminAuditResult
{
    public const string SUCCESS = "success";
    public const string FAILURE = "failure";
}
