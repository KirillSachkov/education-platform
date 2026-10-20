using AccessService.Contracts.Billing;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>Retain the admin URL while preventing sales from being re-enabled or rewriting history.</summary>
public sealed class UpdateBillingConfigEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/access/billing-config/", async Task<EndpointResult<BillingConfigDto>> (
                [FromBody] UpdateBillingConfigRequest request,
                [FromServices] UpdateBillingConfigHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new UpdateBillingConfigCommand(request.IsEnabled), ct))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed record UpdateBillingConfigCommand(bool IsEnabled) : ICommand;

public sealed class UpdateBillingConfigHandler
    : ICommandHandler<BillingConfigDto, UpdateBillingConfigCommand>
{
    public Task<Result<BillingConfigDto, Error>> Handle(
        UpdateBillingConfigCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<BillingConfigDto, Error>>(command.IsEnabled
            ? AccessService.Domain.AccessErrors.SalesClosed()
            : new BillingConfigDto(false));
}