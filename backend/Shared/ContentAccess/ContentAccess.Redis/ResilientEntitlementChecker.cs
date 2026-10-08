using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ContentAccess.Redis;

public sealed class ResilientEntitlementChecker : IEntitlementChecker
{
    private readonly RedisEntitlementChecker _inner;
    private readonly ILogger<ResilientEntitlementChecker> _logger;

    public ResilientEntitlementChecker(
        RedisEntitlementChecker inner,
        ILogger<ResilientEntitlementChecker> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<AccessDecision> CheckAccessAsync(
        AccessSubject subject, string resourceType, Guid resourceId, CancellationToken ct = default)
    {
        try
        {
            AccessDecision decision = await _inner.CheckAccessAsync(subject, resourceType, resourceId, ct);

            if (!decision.IsGranted && subject.IsAuthenticated)
            {
                _logger.LogWarning(
                    "Entitlement denied: User {UserId} for {ResourceType}:{ResourceId}, reason: {Reason}",
                    subject.UserId, resourceType, resourceId, decision.Reason);
            }

            return decision;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable during entitlement check for {ResourceType}:{ResourceId}",
                resourceType, resourceId);
            return AccessDecision.Denied();
        }
    }

    public async Task<IReadOnlyDictionary<Guid, AccessDecision>> CheckAccessBatchAsync(
        AccessSubject subject,
        string resourceType,
        IReadOnlyList<Guid> resourceIds,
        CancellationToken ct = default)
    {
        try
        {
            return await _inner.CheckAccessBatchAsync(subject, resourceType, resourceIds, ct);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable during batch entitlement check for {ResourceType}, {Count} ids",
                resourceType, resourceIds.Count);
            AccessDecision denied = AccessDecision.Denied();
            return resourceIds.ToDictionary(id => id, _ => denied);
        }
    }

    public async Task<IReadOnlySet<Guid>> GetUserEnrolledCourseIdsAsync(
        Guid userId,
        bool includeTrial,
        CancellationToken ct = default)
    {
        try
        {
            return await _inner.GetUserEnrolledCourseIdsAsync(userId, includeTrial, ct);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable while listing enrolled courses for user {UserId}", userId);
            return new HashSet<Guid>();
        }
    }

    public async Task<bool> HasCapabilityAsync(
        AccessSubject subject,
        string capabilityName,
        CancellationToken ct = default)
    {
        try
        {
            return await _inner.HasCapabilityAsync(subject, capabilityName, ct);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(
                ex, "Redis unavailable during capability check {Capability} for user {UserId}",
                capabilityName, subject.UserId);
            // Fail-closed: при недоступности Redis отказываем в действии.
            return false;
        }
    }
}
