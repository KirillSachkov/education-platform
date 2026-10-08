namespace ContentAccess;

public interface IEntitlementChecker
{
    Task<AccessDecision> CheckAccessAsync(
        AccessSubject subject, string resourceType, Guid resourceId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, AccessDecision>> CheckAccessBatchAsync(
        AccessSubject subject,
        string resourceType,
        IReadOnlyList<Guid> resourceIds,
        CancellationToken ct = default);

    Task<IReadOnlySet<Guid>> GetUserEnrolledCourseIdsAsync(
        Guid userId,
        bool includeTrial,
        CancellationToken ct = default);

    /// <summary>
    /// Проверяет, есть ли у пользователя capability tag в его user-grants set'е.
    /// Capability-теги выставляются в Redis при создании plan-grant'а
    /// (<see cref="GrantTags.Capability"/>) на основе <c>Plan.Capabilities</c> bitmask'а.
    /// Используется для гейтинга действий: SubmitIssue требует <c>SUBMIT_ISSUES</c>,
    /// CommunityAccess — <c>COMMUNITY_ACCESS</c> и т.д.
    /// Admin/Author bypass: всегда <c>true</c>.
    /// </summary>
    Task<bool> HasCapabilityAsync(
        AccessSubject subject,
        string capabilityName,
        CancellationToken ct = default);
}
