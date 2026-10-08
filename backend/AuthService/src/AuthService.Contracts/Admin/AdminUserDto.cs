namespace AuthService.Contracts.Admin;

public record AdminUserSummaryDto(
    Guid Id,
    string? UserName,
    string? DisplayName,
    string? Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    bool IsLockedOut,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    Guid? AvatarId);

public record AdminUserDetailDto(
    Guid Id,
    string? UserName,
    string? DisplayName,
    string? Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastLoginAt,
    string? Bio,
    ProfilesDto? Profiles,
    Guid? AvatarId,
    // Telegram @handle привязанного аккаунта (пусто если не привязан) — контакт для автора/админа. #575.
    string? TelegramUsername = null);

public record AdminStatsResponse(
    long TotalUsers,
    long NewToday,
    long NewThisWeek,
    long NewThisMonth,
    long ActiveThisWeek,
    long ConfirmedEmailCount,
    long LockedCount,
    IReadOnlyDictionary<string, long> ByRole,
    IReadOnlyList<DailyRegistrationDto> DailyRegistrations,
    DateOnly RangeFrom,
    DateOnly RangeTo,
    long NewInRange);

public record DailyRegistrationDto(DateTime Day, long Count);

public record AdminAuditLogEntryDto(
    Guid Id,
    Guid AdminId,
    string? AdminUsername,
    string? AdminDisplayName,
    Guid? TargetUserId,
    string Action,
    string Method,
    string Path,
    string? PayloadJson,
    string Result,
    string? ErrorMessage,
    DateTime CreatedAt,
    string? IpAddress,
    string? UserAgent);

public record AdminAuditLogPageResponse(
    IReadOnlyList<AdminAuditLogEntryDto> Items,
    string? NextCursor);

public record AdminBulkLockoutRequest(
    IReadOnlyList<Guid> UserIds,
    bool IsLocked,
    DateTimeOffset? LockoutEnd);

public record AdminBulkRolesRequest(
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<string> Add,
    IReadOnlyList<string> Remove);

public record AdminBulkActionFailureDto(Guid UserId, string Reason);

public record AdminBulkActionResponse(
    IReadOnlyList<Guid> Succeeded,
    IReadOnlyList<AdminBulkActionFailureDto> Failed);
