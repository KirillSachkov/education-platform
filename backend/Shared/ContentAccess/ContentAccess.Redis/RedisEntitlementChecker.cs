using System.Diagnostics;
using StackExchange.Redis;

namespace ContentAccess.Redis;

public sealed class RedisEntitlementChecker : IEntitlementChecker
{
    private const string COURSE_TAG_PREFIX = "course:";
    private const string TRIAL_TAG_SUFFIX = ":trial";

    private readonly IConnectionMultiplexer _redis;

    public RedisEntitlementChecker(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<AccessDecision> CheckAccessAsync(
        AccessSubject subject, string resourceType, Guid resourceId, CancellationToken ct = default)
    {
        using Activity? activity = ContentAccessDiagnostics.ActivitySource.StartActivity(
            "contentaccess.check", ActivityKind.Internal);
        activity?.SetTag("resource.type", resourceType);
        activity?.SetTag("resource.id", resourceId);
        activity?.SetTag("user.authenticated", subject.IsAuthenticated);
        activity?.SetTag("user.admin", subject.IsAdmin);

        if (subject.IsAdmin)
        {
            AccessDecision adminDecision = AccessDecision.Granted(AccessReason.ADMIN_OR_AUTHOR);
            TagDecision(activity, adminDecision, roundTrips: 0);
            return adminDecision;
        }

        IDatabase db = _redis.GetDatabase();
        RedisKey resourceKey = EntitlementKeys.ResourceAccess(resourceType, resourceId);

        int roundTrips = 0;

        RedisValue[] resourceTags = await db.SetMembersAsync(resourceKey);
        roundTrips++;
        activity?.SetTag("resource.tags.size", resourceTags.Length);

        // Fail-closed: пустой или отсутствующий набор тегов означает, что ресурс
        // не зарегистрирован в access-индексе Redis (ещё не синхронизирован,
        // удалён, или bug в sync-handler). Отказ по умолчанию предотвращает
        // утечку контента при расшатавшемся состоянии Redis.
        if (resourceTags.Length == 0)
        {
            AccessDecision denied = AccessDecision.Denied(AccessReason.RESOURCE_NOT_REGISTERED);
            TagDecision(activity, denied, roundTrips);
            return denied;
        }

        bool hasPublicTag = false;
        bool hasAuthenticatedTag = false;
        foreach (RedisValue tagValue in resourceTags)
        {
            if (tagValue == GrantTags.PUBLIC)
            {
                hasPublicTag = true;
                break;
            }

            if (tagValue == GrantTags.AUTHENTICATED)
                hasAuthenticatedTag = true;
        }

        if (hasPublicTag)
        {
            AccessDecision publicDecision = AccessDecision.Granted(AccessReason.PUBLIC);
            TagDecision(activity, publicDecision, roundTrips);
            return publicDecision;
        }

        if (!subject.IsAuthenticated)
        {
            AccessDecision anonDenied = AccessDecision.Denied(AccessReason.NOT_AUTHENTICATED);
            TagDecision(activity, anonDenied, roundTrips);
            return anonDenied;
        }

        if (hasAuthenticatedTag)
        {
            AccessDecision authDecision = AccessDecision.Granted(AccessReason.AUTHENTICATED_ONLY);
            TagDecision(activity, authDecision, roundTrips);
            return authDecision;
        }

        RedisKey userKey = EntitlementKeys.UserGrants(subject.UserId);
        RedisValue[] overlap = await db.SetCombineAsync(SetOperation.Intersect, resourceKey, userKey);
        roundTrips++;

        AccessDecision decision = overlap.Length > 0
            ? AccessDecision.Granted(AccessReason.ENTITLEMENT)
            : AccessDecision.Denied();

        TagDecision(activity, decision, roundTrips);
        return decision;
    }

    public async Task<IReadOnlyDictionary<Guid, AccessDecision>> CheckAccessBatchAsync(
        AccessSubject subject,
        string resourceType,
        IReadOnlyList<Guid> resourceIds,
        CancellationToken ct = default)
    {
        if (resourceIds.Count == 0)
            return new Dictionary<Guid, AccessDecision>();

        using Activity? activity = ContentAccessDiagnostics.ActivitySource.StartActivity(
            "contentaccess.check_batch", ActivityKind.Internal);
        activity?.SetTag("resource.type", resourceType);
        activity?.SetTag("batch.size", resourceIds.Count);
        activity?.SetTag("user.authenticated", subject.IsAuthenticated);
        activity?.SetTag("user.admin", subject.IsAdmin);

        if (subject.IsAdmin)
        {
            AccessDecision grantedAdmin = AccessDecision.Granted(AccessReason.ADMIN_OR_AUTHOR);
            Dictionary<Guid, AccessDecision> adminResult =
                resourceIds.ToDictionary(id => id, _ => grantedAdmin);
            TagBatchOutcome(activity, adminResult, roundTrips: 0);
            return adminResult;
        }

        IDatabase db = _redis.GetDatabase();
        int roundTrips = 0;

        // Fetch user grants once (if authenticated).
        HashSet<string> userGrants = new(StringComparer.Ordinal);
        if (subject.IsAuthenticated)
        {
            RedisValue[] grants = await db.SetMembersAsync(EntitlementKeys.UserGrants(subject.UserId));
            roundTrips++;
            activity?.SetTag("user.grants.size", grants.Length);

            foreach (RedisValue v in grants)
            {
                string? s = v.ToString();
                if (!string.IsNullOrEmpty(s))
                    userGrants.Add(s);
            }
        }

        // Batch SMEMBERS for all resource tag sets.
        IBatch batch = db.CreateBatch();
        Task<RedisValue[]>[] tasks = new Task<RedisValue[]>[resourceIds.Count];
        for (int i = 0; i < resourceIds.Count; i++)
            tasks[i] = batch.SetMembersAsync(EntitlementKeys.ResourceAccess(resourceType, resourceIds[i]));
        batch.Execute();
        RedisValue[][] tagsByIndex = await Task.WhenAll(tasks);
        roundTrips++;

        Dictionary<Guid, AccessDecision> result = new(resourceIds.Count);
        for (int i = 0; i < resourceIds.Count; i++)
        {
            Guid id = resourceIds[i];
            RedisValue[] tags = tagsByIndex[i];

            // Fail-closed: тот же контракт, что и в CheckAccessAsync.
            if (tags.Length == 0)
            {
                result[id] = AccessDecision.Denied(AccessReason.RESOURCE_NOT_REGISTERED);
                continue;
            }

            bool hasPublicTag = false;
            bool hasAuthenticatedTag = false;
            bool hasOverlap = false;

            foreach (RedisValue tagValue in tags)
            {
                string? tag = tagValue.ToString();
                if (string.IsNullOrEmpty(tag))
                    continue;

                if (string.Equals(tag, GrantTags.PUBLIC, StringComparison.Ordinal))
                {
                    hasPublicTag = true;
                    break;
                }

                if (string.Equals(tag, GrantTags.AUTHENTICATED, StringComparison.Ordinal))
                {
                    hasAuthenticatedTag = true;
                    continue;
                }

                if (subject.IsAuthenticated && userGrants.Contains(tag))
                {
                    hasOverlap = true;
                }
            }

            if (hasPublicTag)
            {
                result[id] = AccessDecision.Granted(AccessReason.PUBLIC);
                continue;
            }

            if (!subject.IsAuthenticated)
            {
                result[id] = AccessDecision.Denied(AccessReason.NOT_AUTHENTICATED);
                continue;
            }

            if (hasAuthenticatedTag)
                result[id] = AccessDecision.Granted(AccessReason.AUTHENTICATED_ONLY);
            else if (hasOverlap)
                result[id] = AccessDecision.Granted(AccessReason.ENTITLEMENT);
            else
                result[id] = AccessDecision.Denied();
        }

        TagBatchOutcome(activity, result, roundTrips);
        return result;
    }

    public async Task<IReadOnlySet<Guid>> GetUserEnrolledCourseIdsAsync(
        Guid userId,
        bool includeTrial,
        CancellationToken ct = default)
    {
        using Activity? activity = ContentAccessDiagnostics.ActivitySource.StartActivity(
            "contentaccess.enrolled_courses", ActivityKind.Internal);
        activity?.SetTag("user.id", userId);
        activity?.SetTag("include_trial", includeTrial);

        HashSet<Guid> result = [];

        if (userId == Guid.Empty)
            return result;

        IDatabase db = _redis.GetDatabase();
        RedisValue[] grants = await db.SetMembersAsync(EntitlementKeys.UserGrants(userId));
        activity?.SetTag("user.grants.size", grants.Length);

        foreach (RedisValue grant in grants)
        {
            string? tag = grant.ToString();
            if (string.IsNullOrEmpty(tag))
                continue;

            if (!tag.StartsWith(COURSE_TAG_PREFIX, StringComparison.Ordinal))
                continue;

            string body = tag[COURSE_TAG_PREFIX.Length..];
            bool isTrial = body.EndsWith(TRIAL_TAG_SUFFIX, StringComparison.Ordinal);
            if (isTrial)
            {
                if (!includeTrial)
                    continue;
                body = body[..^TRIAL_TAG_SUFFIX.Length];
            }

            if (Guid.TryParseExact(body, "D", out Guid courseId))
                result.Add(courseId);
        }

        activity?.SetTag("enrolled_courses.count", result.Count);
        return result;
    }

    public async Task<bool> HasCapabilityAsync(
        AccessSubject subject,
        string capabilityName,
        CancellationToken ct = default)
    {
        using Activity? activity = ContentAccessDiagnostics.ActivitySource.StartActivity(
            "contentaccess.has_capability", ActivityKind.Internal);
        activity?.SetTag("capability.name", capabilityName);
        activity?.SetTag("user.authenticated", subject.IsAuthenticated);
        activity?.SetTag("user.admin", subject.IsAdmin);

        // Admin / Author bypass — у автора всегда полные права на свой контент.
        if (subject.IsAdmin)
        {
            activity?.SetTag("decision.granted", true);
            return true;
        }

        if (!subject.IsAuthenticated || subject.UserId == Guid.Empty)
        {
            activity?.SetTag("decision.granted", false);
            return false;
        }

        IDatabase db = _redis.GetDatabase();
        bool has = await db.SetContainsAsync(
            EntitlementKeys.UserGrants(subject.UserId),
            GrantTags.Capability(capabilityName));
        activity?.SetTag("decision.granted", has);
        return has;
    }

    private static void TagDecision(Activity? activity, AccessDecision decision, int roundTrips)
    {
        activity?.SetTag("decision.granted", decision.IsGranted);
        activity?.SetTag("decision.reason", decision.Reason.ToString());
        activity?.SetTag("redis.round_trips", roundTrips);
    }

    private static void TagBatchOutcome(
        Activity? activity,
        IReadOnlyDictionary<Guid, AccessDecision> decisions,
        int roundTrips)
    {
        int granted = 0;
        foreach (AccessDecision d in decisions.Values)
        {
            if (d.IsGranted)
                granted++;
        }

        activity?.SetTag("redis.round_trips", roundTrips);
        activity?.SetTag("batch.granted", granted);
        activity?.SetTag("batch.denied", decisions.Count - granted);
    }
}
