using AccessService.Core.Database;
using AccessService.Core.Features.Plans;
using AccessService.Domain;
using ContentAccess;
using Microsoft.Extensions.Options;

namespace AccessService.Core.Features.PlanGrants.IntegrationEvents;

public interface IUserGrantProjection
{
    Task RecalculateAsync(Guid userId, CancellationToken cancellationToken);

    Task RecalculateManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

public sealed class UserGrantProjection : IUserGrantProjection
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IUserGrantWriter _writer;
    private readonly IOptions<AccessOptions> _options;
    private readonly TimeProvider _timeProvider;

    public UserGrantProjection(
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IUserGrantWriter writer,
        IOptions<AccessOptions> options,
        TimeProvider timeProvider)
    {
        _grants = grants;
        _plans = plans;
        _writer = writer;
        _options = options;
        _timeProvider = timeProvider;
    }

    public Task RecalculateAsync(Guid userId, CancellationToken cancellationToken) =>
        RecalculateManyAsync([userId], cancellationToken);

    public async Task RecalculateManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        Guid[] distinctUserIds = userIds.Distinct().ToArray();
        if (distinctUserIds.Length == 0)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        IReadOnlyList<PlanGrant> activeGrants = await _grants.GetManyByAsync(
            grant => distinctUserIds.Contains(grant.UserId)
                     && grant.Status == PlanGrantStatus.ACTIVE
                     && (grant.ExpiresAt == null || grant.ExpiresAt > now),
            cancellationToken);

        Guid[] planIds = activeGrants.Select(grant => grant.PlanId).Distinct().ToArray();
        IReadOnlyList<Plan> plans = planIds.Length == 0
            ? []
            : await _plans.GetManyByAsync(plan => planIds.Contains(plan.Id), cancellationToken);
        Dictionary<Guid, Plan> plansById = plans.ToDictionary(plan => plan.Id);

        ILookup<Guid, PlanGrant> grantsByUser = activeGrants.ToLookup(grant => grant.UserId);
        foreach (Guid userId in distinctUserIds)
        {
            IReadOnlyList<string> tags = PlanGrantTagCalculator.CalculateUnion(
                grantsByUser[userId].ToList(),
                plansById,
                _options.Value.FullPlatformGrantsTrainerPro);
            await _writer.ReplaceAsync(userId, tags, cancellationToken);
        }
    }
}
