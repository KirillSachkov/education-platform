namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

/// <summary>
///     Internal ProgressService message: asks a local Wolverine handler to revoke Redis
///     content-access tags for the given users on the given course.
///     Published via the durable outbox inside the transaction that deletes course enrollments —
///     that way the Redis revocation is crash-safe (Wolverine replays the message until it succeeds).
/// </summary>
public sealed record CourseContentAccessRevocationRequested(
    Guid CourseId,
    IReadOnlyCollection<Guid> UserIds);
