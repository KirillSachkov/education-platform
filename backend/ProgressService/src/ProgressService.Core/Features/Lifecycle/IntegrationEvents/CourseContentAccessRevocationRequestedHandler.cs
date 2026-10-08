using ContentAccess;

namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

/// <summary>
///     Consumes <see cref="CourseContentAccessRevocationRequested" /> and removes the course tag
///     from every affected user's Redis grant set. If Redis is unreachable, Wolverine's standard
///     error policies will retry the message (see <c>ConfigureStandardErrorPolicies</c>) — so the
///     post-delete Redis cleanup becomes crash-safe.
/// </summary>
public static class CourseContentAccessRevocationRequestedHandler
{
    public static async Task HandleAsync(
        CourseContentAccessRevocationRequested message,
        IUserGrantWriter userGrantWriter,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        string courseTag = GrantTags.Course(message.CourseId);

        foreach (Guid userId in message.UserIds)
        {
            // RevokeAsync is idempotent — a second delivery is a no-op.
            await userGrantWriter.RevokeAsync(userId, courseTag, cancellationToken);
        }

        logger.LogInformation(
            "Revoked Redis course tag for {UserCount} users after hard-delete. CourseId={CourseId}",
            message.UserIds.Count,
            message.CourseId);
    }
}

