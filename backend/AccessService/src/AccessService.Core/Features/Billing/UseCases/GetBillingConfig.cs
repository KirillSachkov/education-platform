using AccessService.Contracts.Billing;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>Sales remain closed independently of historical billing configuration.</summary>
public sealed class GetBillingConfigEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/billing-config/", async Task<EndpointResult<BillingConfigDto>> (
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
    public Task<Result<BillingConfigDto, Error>> Handle(
        GetBillingConfigQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Result<BillingConfigDto, Error>>(new BillingConfigDto(false));
}