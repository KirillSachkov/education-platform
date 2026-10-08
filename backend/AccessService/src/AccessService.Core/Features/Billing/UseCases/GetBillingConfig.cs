using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Domain.Billing;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>
///     <c>GET /access/billing-config</c> — публичный рантайм-флаг «включён ли приём прямой
///     оплаты». Фронт читает его, чтобы показать кнопку «Оплатить» (T-Bank) либо fallback
///     на Telegram. Значение = ряд <c>billing_config</c>, либо дефолт из конфига если ряда нет.
/// </summary>
public sealed class GetBillingConfigEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/billing-config", async Task<EndpointResult<BillingConfigDto>> (
                [FromServices] GetBillingConfigHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetBillingConfigQuery(), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed record GetBillingConfigQuery() : IQuery;

public sealed class GetBillingConfigHandler
    : IQueryHandlerWithResult<BillingConfigDto, GetBillingConfigQuery>
{
    private readonly IBillingConfigRepository _repository;
    private readonly BillingOptions _options;

    public GetBillingConfigHandler(IBillingConfigRepository repository, IOptions<BillingOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    public async Task<Result<BillingConfigDto, Error>> Handle(
        GetBillingConfigQuery query,
        CancellationToken cancellationToken = default)
    {
        BillingConfig? config = await _repository.GetAsync(cancellationToken);
        bool enabled = config?.IsEnabled ?? _options.DefaultEnabled;
        return new BillingConfigDto(enabled);
    }
}
