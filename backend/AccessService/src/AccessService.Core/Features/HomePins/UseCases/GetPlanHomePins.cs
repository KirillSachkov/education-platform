using AccessService.Contracts.HomePins;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.HomePins;
using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.HomePins.UseCases;

/// <summary>
///     Author-side список закрепов плана. Title обогащается через ECS; при недоступности
///     ECS title soft-degrade'ит в пустую строку (список всё равно рендерится). Epic #397.
/// </summary>
public sealed class GetPlanHomePinsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/{planId:guid}/home-pins/",
                async Task<EndpointResult<IReadOnlyList<HomePinListItemDto>>> (
                    [FromRoute] Guid planId,
                    [FromServices] GetPlanHomePinsHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetPlanHomePinsQuery(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record GetPlanHomePinsQuery(Guid PlanId) : IQuery;

public sealed class GetPlanHomePinsHandler
    : IQueryHandlerWithResult<IReadOnlyList<HomePinListItemDto>, GetPlanHomePinsQuery>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanPinnedMaterialRepository _pins;
    private readonly IEducationContentServiceClient _ecs;
    private readonly UserScopedData _user;
    private readonly ILogger<GetPlanHomePinsHandler> _logger;

    public GetPlanHomePinsHandler(
        IPlansRepository plans,
        IPlanPinnedMaterialRepository pins,
        IEducationContentServiceClient ecs,
        UserScopedData user,
        ILogger<GetPlanHomePinsHandler> logger)
    {
        _plans = plans;
        _pins = pins;
        _ecs = ecs;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<HomePinListItemDto>, Error>> Handle(
        GetPlanHomePinsQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == query.PlanId, cancellationToken);
        if (planResult.IsFailure) return planResult.Error;
        if (!_user.IsOwnerOrAdmin(planResult.Value.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        IReadOnlyList<PlanPinnedMaterial> pins = await _pins.GetByPlanAsync(query.PlanId, cancellationToken);
        if (pins.Count == 0)
        {
            return Result.Success<IReadOnlyList<HomePinListItemDto>, Error>([]);
        }

        Dictionary<Guid, string> titlesById = await ResolveTitlesAsync(pins, cancellationToken);

        IReadOnlyList<HomePinListItemDto> result = pins
            .Select(p => new HomePinListItemDto(
                p.Id,
                p.MaterialId,
                titlesById.TryGetValue(p.MaterialId, out string? title) ? title : string.Empty,
                p.Note,
                p.SortKey.Value))
            .ToList();

        return Result.Success<IReadOnlyList<HomePinListItemDto>, Error>(result);
    }

    private async Task<Dictionary<Guid, string>> ResolveTitlesAsync(
        IReadOnlyList<PlanPinnedMaterial> pins,
        CancellationToken cancellationToken)
    {
        Guid[] ids = pins.Select(p => p.MaterialId).Distinct().ToArray();

        Result<IReadOnlyList<MaterialSummaryDto>, Error> summaries =
            await _ecs.GetMaterialSummariesAsync(ids, cancellationToken);

        if (summaries.IsFailure)
        {
            // Soft-degrade: title пустой, список всё равно отдаём.
            _logger.LogWarning(
                "ECS GetMaterialSummaries failed for home-pins list ({Error}); titles degrade to empty",
                summaries.Error.GetMessage());
            return [];
        }

        return summaries.Value.ToDictionary(s => s.Id, s => s.Title);
    }
}
