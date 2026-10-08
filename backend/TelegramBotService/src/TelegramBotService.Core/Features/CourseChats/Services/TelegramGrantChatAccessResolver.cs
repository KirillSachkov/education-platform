using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
/// Resolves active grants to the exact PlanIds whose Telegram bindings they authorize.
/// Capability and trial-to-canonical mapping stay authoritative in AccessService.
/// </summary>
public static class TelegramGrantChatAccessResolver
{
    public const string COMMUNITY_ACCESS = "COMMUNITY_ACCESS";
    private const string GRANT_STATUS_ACTIVE = "ACTIVE";

    public static async Task<Result<IReadOnlyDictionary<Guid, PlanGrantDto>, Error>> ResolveAsync(
        IEnumerable<PlanGrantDto> grants,
        IAccessServiceClient accessClient,
        CancellationToken cancellationToken)
    {
        PlanGrantDto[] activeGrants = grants
            .Where(grant => string.Equals(grant.Status, GRANT_STATUS_ACTIVE, StringComparison.Ordinal))
            .ToArray();

        if (activeGrants.Length == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, PlanGrantDto>, Error>(
                new Dictionary<Guid, PlanGrantDto>());
        }

        var result = new Dictionary<Guid, PlanGrantDto>();
        var legacyGrants = new List<PlanGrantDto>();
        foreach (PlanGrantDto grant in activeGrants)
        {
            if (grant.Capabilities is null)
            {
                legacyGrants.Add(grant);
            }
            else if (grant.Capabilities.Contains(COMMUNITY_ACCESS, StringComparer.Ordinal)
                     && grant.TelegramBindingPlanId is { } bindingPlanId)
            {
                result.TryAdd(bindingPlanId, grant);
            }
        }

        Task<(PlanGrantDto Grant, Result<PlanTelegramInfoDto, Error> Info)>[] lookupTasks = legacyGrants
            .Select(async grant => (
                grant,
                await accessClient.GetPlanTelegramInfoAsync(grant.PlanId, cancellationToken)))
            .ToArray();

        (PlanGrantDto Grant, Result<PlanTelegramInfoDto, Error> Info)[] lookups =
            await Task.WhenAll(lookupTasks);

        foreach ((PlanGrantDto grant, Result<PlanTelegramInfoDto, Error> infoResult) in lookups)
        {
            if (infoResult.IsFailure)
                return infoResult.Error;

            PlanTelegramInfoDto info = infoResult.Value;
            if (info.Capabilities is null
                || !info.Capabilities.Contains(COMMUNITY_ACCESS, StringComparer.Ordinal))
            {
                continue;
            }

            Guid bindingPlanId = info.CanonicalTelegramPlanId ?? grant.PlanId;
            result.TryAdd(bindingPlanId, grant);
        }

        return Result.Success<IReadOnlyDictionary<Guid, PlanGrantDto>, Error>(result);
    }
}
