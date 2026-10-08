using System.Security.Cryptography;
using System.Text;
using AuthService.Contracts.Admin;

namespace AuthService.Core.Services;

/// <summary>Marker class for structured audit logging. SourceContext = "AuthAudit".</summary>
public sealed class AuthAudit;

public static class AuthAuditLog
{
    /// <summary>
    /// Short stable hash of an email for audit correlation.
    /// Emails are PII — we log a 12-char SHA-256 prefix so log searches still correlate
    /// events for the same address, without exposing the address itself.
    /// </summary>
    private static string HashEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "unknown";
        }

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12];
    }

    public static void LogLoginSuccess(this ILogger<AuthAudit> logger, Guid userId, string method) =>
        logger.LogInformation(
            "Login succeeded for user {UserId} via {Method} | EventType={EventType}",
            userId, method, "login_success");

    public static void LogLoginFailed(this ILogger<AuthAudit> logger, string email, string reason) =>
        logger.LogWarning(
            "Login failed for email-hash {EmailHash}: {Reason} | EventType={EventType}",
            HashEmail(email), reason, "login_failed");

    public static void LogOtpRequested(this ILogger<AuthAudit> logger, Guid? userId, bool? isNewUser) =>
        logger.LogInformation(
            "OTP requested for user {UserId} (newUser={IsNewUser}) | EventType={EventType}",
            userId, isNewUser, "otp_requested");

    public static void LogRegistration(this ILogger<AuthAudit> logger, Guid userId, string method) =>
        logger.LogInformation(
            "User {UserId} registered via {Method} | EventType={EventType}",
            userId, method, "registration");

    public static void LogPasswordChanged(this ILogger<AuthAudit> logger, Guid userId) =>
        logger.LogInformation(
            "Password changed for user {UserId} | EventType={EventType}",
            userId, "password_changed");

    public static void LogPasswordSet(this ILogger<AuthAudit> logger, Guid userId) =>
        logger.LogInformation(
            "Password set for user {UserId} | EventType={EventType}",
            userId, "password_set");

    public static void LogPasswordResetRequested(this ILogger<AuthAudit> logger, string email) =>
        logger.LogInformation(
            "Password reset requested for email-hash {EmailHash} | EventType={EventType}",
            HashEmail(email), "password_reset_requested");

    public static void LogPasswordReset(this ILogger<AuthAudit> logger, Guid userId) =>
        logger.LogInformation(
            "Password reset completed for user {UserId} | EventType={EventType}",
            userId, "password_reset");

    public static void LogSessionsRevoked(this ILogger<AuthAudit> logger, Guid userId) =>
        logger.LogInformation(
            "All sessions revoked for user {UserId} | EventType={EventType}",
            userId, "sessions_revoked");

    public static void LogGitHubLinked(this ILogger<AuthAudit> logger, Guid userId) =>
        logger.LogInformation(
            "GitHub account linked for user {UserId} | EventType={EventType}",
            userId, "github_linked");

    public static void LogGitHubUnlinked(this ILogger<AuthAudit> logger, Guid userId) =>
        logger.LogInformation(
            "GitHub account unlinked for user {UserId} | EventType={EventType}",
            userId, "github_unlinked");

    public static void LogGitHubUnlinkFailed(
        this ILogger<AuthAudit> logger, Guid userId, string errors) =>
        logger.LogWarning(
            "Failed to unlink GitHub account for user {UserId}: {Errors} | EventType={EventType}",
            userId, errors, "github_unlink_failed");

    public static void LogAdminUserDeleted(this ILogger<AuthAudit> logger, Guid adminId, Guid targetUserId) =>
        logger.LogWarning(
            "Admin {AdminId} deleted user {TargetUserId} | EventType={EventType}",
            adminId, targetUserId, "admin_user_deleted");

    public static void LogAdminLockoutChanged(this ILogger<AuthAudit> logger, Guid adminId, Guid targetUserId, bool isLocked) =>
        logger.LogInformation(
            "Admin {AdminId} {Action} user {TargetUserId} | EventType={EventType}",
            adminId, isLocked ? "locked" : "unlocked", targetUserId, "admin_lockout_changed");

    public static void LogAdminRolesChanged(this ILogger<AuthAudit> logger, Guid adminId, Guid targetUserId, IReadOnlyList<string> roles) =>
        logger.LogInformation(
            "Admin {AdminId} set roles [{Roles}] for user {TargetUserId} | EventType={EventType}",
            adminId, string.Join(", ", roles), targetUserId, "admin_roles_changed");

    public static void LogAdminUserUpdated(
        this ILogger<AuthAudit> logger,
        Guid adminId,
        Guid targetUserId,
        AdminUpdateUserRequest request)
    {
        List<string> fields = [];
        if (!string.IsNullOrWhiteSpace(request.Username)) fields.Add("username");
        if (!string.IsNullOrWhiteSpace(request.Email)) fields.Add("email");
        if (request.EmailConfirmed.HasValue) fields.Add("emailConfirmed");

        logger.LogInformation(
            "Admin {AdminId} updated user {TargetUserId} fields [{ChangedFields}] | EventType={EventType}",
            adminId, targetUserId, string.Join(", ", fields), "admin_user_updated");
    }

    public static void LogGitHubSyncCompleted(
        this ILogger<AuthAudit> logger, Guid userId, int count) =>
        logger.LogInformation(
            "GitHub course sync completed for user {UserId}: {Count} courses synced | EventType={EventType}",
            userId, count, "github_sync_completed");

    public static void LogGitHubSyncFailed(
        this ILogger<AuthAudit> logger, Exception ex, Guid userId) =>
        logger.LogWarning(
            ex,
            "GitHub course auto-sync failed during login for user {UserId} | EventType={EventType}",
            userId, "github_sync_failed");

    public static void LogGitHubDisabledLoginAttempt(
        this ILogger<AuthAudit> logger, string surface) =>
        logger.LogInformation(
            "GitHub login attempt blocked — login via GitHub is disabled (#696), surface {Surface} | EventType={EventType}",
            surface, "github_login_disabled_attempt");

    public static void LogTelegramLinkTokenIssued(this ILogger<AuthAudit> logger, Guid userId) =>
        logger.LogInformation(
            "Telegram link token issued for user {UserId} | EventType={EventType}",
            userId, "telegram_link_token_issued");

    public static void LogTelegramLinked(
        this ILogger<AuthAudit> logger, Guid userId, long telegramUserId) =>
        logger.LogInformation(
            "Telegram account {TelegramUserId} linked for user {UserId} | EventType={EventType}",
            telegramUserId, userId, "telegram_linked");

    public static void LogTelegramUnlinked(
        this ILogger<AuthAudit> logger, Guid userId, long telegramUserId) =>
        logger.LogInformation(
            "Telegram account {TelegramUserId} unlinked for user {UserId} | EventType={EventType}",
            telegramUserId, userId, "telegram_unlinked");

    public static void LogTelegramUnlinkFailed(
        this ILogger<AuthAudit> logger, Guid userId, string errors) =>
        logger.LogWarning(
            "Failed to unlink Telegram account for user {UserId}: {Errors} | EventType={EventType}",
            userId, errors, "telegram_unlink_failed");
}
