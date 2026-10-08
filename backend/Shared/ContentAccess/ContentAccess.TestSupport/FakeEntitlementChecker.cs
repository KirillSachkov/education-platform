namespace ContentAccess.TestSupport;

public sealed class FakeEntitlementChecker : IEntitlementChecker
{
    private bool _grantAll = true;
    private readonly HashSet<string> _deniedResourceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string ResourceType, Guid ResourceId), AccessDecision> _perResource = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _standardEnrollments = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _trialEnrollments = [];

    public void GrantAll() => _grantAll = true;

    public void DenyAll() => _grantAll = false;

    /// <summary>
    /// Denies access for a specific resource type while granting everything else.
    /// Useful for testing enrollment gates where entity access is granted
    /// but course-level access is denied.
    /// </summary>
    public void DenyResourceType(string resourceType) => _deniedResourceTypes.Add(resourceType);

    /// <summary>
    /// Overrides the decision for a specific (resourceType, resourceId). Used to simulate
    /// per-material access-type gating in feed tests.
    /// </summary>
    public void SetDecision(string resourceType, Guid resourceId, AccessDecision decision)
        => _perResource[(resourceType, resourceId)] = decision;

    public void EnrollStandard(Guid userId, Guid courseId)
    {
        if (!_standardEnrollments.TryGetValue(userId, out HashSet<Guid>? set))
            _standardEnrollments[userId] = set = [];
        set.Add(courseId);
    }

    public void EnrollTrial(Guid userId, Guid courseId)
    {
        if (!_trialEnrollments.TryGetValue(userId, out HashSet<Guid>? set))
            _trialEnrollments[userId] = set = [];
        set.Add(courseId);
    }

    public void Reset()
    {
        _grantAll = true;
        _deniedResourceTypes.Clear();
        _perResource.Clear();
        _standardEnrollments.Clear();
        _trialEnrollments.Clear();
        _capabilities.Clear();
        _deniedCapabilities.Clear();
    }

    public Task<AccessDecision> CheckAccessAsync(
        AccessSubject subject, string resourceType, Guid resourceId, CancellationToken ct = default)
        => Task.FromResult(Decide(subject, resourceType, resourceId));

    public Task<IReadOnlyDictionary<Guid, AccessDecision>> CheckAccessBatchAsync(
        AccessSubject subject,
        string resourceType,
        IReadOnlyList<Guid> resourceIds,
        CancellationToken ct = default)
    {
        Dictionary<Guid, AccessDecision> result = new(resourceIds.Count);
        foreach (Guid id in resourceIds)
            result[id] = Decide(subject, resourceType, id);
        return Task.FromResult<IReadOnlyDictionary<Guid, AccessDecision>>(result);
    }

    public Task<IReadOnlySet<Guid>> GetUserEnrolledCourseIdsAsync(
        Guid userId,
        bool includeTrial,
        CancellationToken ct = default)
    {
        HashSet<Guid> result = [];
        if (_standardEnrollments.TryGetValue(userId, out HashSet<Guid>? standard))
            foreach (Guid id in standard) result.Add(id);
        if (includeTrial && _trialEnrollments.TryGetValue(userId, out HashSet<Guid>? trial))
            foreach (Guid id in trial) result.Add(id);
        return Task.FromResult<IReadOnlySet<Guid>>(result);
    }

    private readonly HashSet<(Guid UserId, string Capability)> _capabilities = [];
    private readonly HashSet<(Guid UserId, string Capability)> _deniedCapabilities = [];

    /// <summary>Помечает что у пользователя есть указанный capability.</summary>
    public void GrantCapability(Guid userId, string capabilityName) =>
        _capabilities.Add((userId, capabilityName));

    /// <summary>Явно запрещает capability для пользователя, даже когда включён GrantAll.</summary>
    public void DenyCapability(Guid userId, string capabilityName) =>
        _deniedCapabilities.Add((userId, capabilityName));

    public Task<bool> HasCapabilityAsync(
        AccessSubject subject,
        string capabilityName,
        CancellationToken ct = default)
    {
        if (subject.IsAdmin)
            return Task.FromResult(true);

        if (_deniedCapabilities.Contains((subject.UserId, capabilityName)))
            return Task.FromResult(false);

        if (_grantAll && !_capabilities.Any(c => c.UserId == subject.UserId))
            return Task.FromResult(true);

        return Task.FromResult(_capabilities.Contains((subject.UserId, capabilityName)));
    }

    private AccessDecision Decide(AccessSubject subject, string resourceType, Guid resourceId)
    {
        if (subject.IsAdmin)
            return AccessDecision.Granted(AccessReason.ADMIN_OR_AUTHOR);

        if (_perResource.TryGetValue((resourceType, resourceId), out AccessDecision? overrideDecision))
            return overrideDecision;

        if (_deniedResourceTypes.Contains(resourceType))
            return AccessDecision.Denied();

        return _grantAll
            ? AccessDecision.Granted(AccessReason.PUBLIC)
            : AccessDecision.Denied();
    }
}
