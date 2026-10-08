using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Core.Features.Billing.UseCases;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.TrainerPro.UseCases;

/// <summary>
/// <c>POST /access/trainer-pro/orders</c> — покупка подписки тренажёра (#674). Тонкий эндпоинт над
/// той же order/payment-машинерией (<see cref="CreateOrderHandler"/>) через общий
/// <see cref="CreateOrderPipeline"/>, но с <see cref="PlanScope.TRAINER"/>: принимает ТОЛЬКО
/// TRAINER-scoped планы (PLATFORM-план отвергается доменным guard'ом <c>order.plan.platform_only</c>).
/// Симметрично платформенный <c>/access/orders/</c> отвергает TRAINER-планы.
/// </summary>
public sealed class CreateTrainerProOrderEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/trainer-pro/orders", async Task<EndpointResult<CreateOrderResponse>> (
                [FromBody] CreateOrderRequest request,
                [FromServices] CreateOrderHandler handler,
                [FromServices] IIdempotencyKeyRepository idempotencyRepo,
                [FromServices] UserScopedData user,
                HttpContext httpContext,
                CancellationToken ct) =>
                await CreateOrderPipeline.ExecuteAsync(
                    request, PlanScope.TRAINER, handler, idempotencyRepo, user, httpContext, ct))
            .RequireAuthorization()
            .RequireRateLimiting("order-create");
    }
}
