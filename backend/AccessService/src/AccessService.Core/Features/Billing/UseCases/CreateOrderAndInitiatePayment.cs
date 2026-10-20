using AccessService.Contracts.Billing;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>Keep the old checkout URL fail-closed without creating orders or contacting a provider.</summary>
public sealed class CreateOrderEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/orders/", async Task<EndpointResult<CreateOrderResponse>> (
                [FromBody] CreateOrderRequest request,
                [FromServices] CreateOrderHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new CreateOrderCommand(request), ct))
            .RequireAuthorization()
            .RequireRateLimiting("order-create");
    }
}

public sealed record CreateOrderCommand(CreateOrderRequest Request) : ICommand;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrderResponse, CreateOrderCommand>
{
    public Task<Result<CreateOrderResponse, Error>> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<CreateOrderResponse, Error>>(AccessErrors.SalesClosed());
}