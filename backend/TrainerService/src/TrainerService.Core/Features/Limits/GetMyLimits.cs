using ContentAccess;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Limits;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain;

namespace TrainerService.Core.Features.Limits;

public sealed record GetMyLimitsQuery(Guid UserId, bool IsAdmin) : IQuery;

public sealed class GetMyLimitsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/me/limits",
                async Task<EndpointResult<TrainerLimitsDto>> (
                    GetMyLimitsHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMyLimitsQuery(user.UserId, user.IsAdmin), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Остаток AI-лимитов вызывающего + статус Trainer Pro (#568) — для карточки «лимиты» в хабе.
///     hasPro резолвится той же политикой, что и гейты (cap:TRAINER_PRO; полный доступ к платформе
///     даёт его авто, #568). Лимиты/использование — из <see cref="TrainerQuotaService"/> (read-only,
///     без инкремента). Free → дневной лимит грейдов + голос/мок 0 (PRO-фича).
/// </summary>
public sealed class GetMyLimitsHandler : IQueryHandlerWithResult<TrainerLimitsDto, GetMyLimitsQuery>
{
    private readonly IEntitlementChecker _entitlements;
    private readonly TrainerQuotaService _quota;

    public GetMyLimitsHandler(IEntitlementChecker entitlements, TrainerQuotaService quota)
    {
        _entitlements = entitlements;
        _quota = quota;
    }

    public async Task<Result<TrainerLimitsDto, Error>> Handle(
        GetMyLimitsQuery query,
        CancellationToken cancellationToken)
    {
        // Админ — Pro-тир (HasProAsync даёт ему PRO), но НЕ безлимит: лимиты действуют для всех (#568).
        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            query.UserId, query.IsAdmin, _entitlements, cancellationToken);

        IReadOnlyList<TrainerQuotaUsage> usage =
            await _quota.GetUsageSnapshotAsync(query.UserId, hasPro, cancellationToken);

        TrainerLimitDto Map(QuotaDimension dimension)
        {
            TrainerQuotaUsage row = usage.First(u => u.Dimension == dimension);
            return new TrainerLimitDto(row.Used, row.Limit);
        }

        return new TrainerLimitsDto(
            IsPro: hasPro,
            OpenGrades: Map(QuotaDimension.OPEN_GRADE),
            Voice: Map(QuotaDimension.VOICE),
            Mock: Map(QuotaDimension.MOCK));
    }
}
