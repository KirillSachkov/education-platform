using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Billing.UseCases.Admin;

/// <summary>
/// POST /access/admin/orders/{id}/refund — STUB. Возвращает 501 Not Implemented.
/// Полная реализация (вызов T-Bank Refund API + revoke grant + Order.Refund)
/// — в Phase F.2.
///
/// До тех пор admin может использовать <c>revoke-grant</c> для отзыва доступа,
/// а refund средств запускать через личный кабинет T-Bank.
/// </summary>
public sealed class RefundOrderEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/access/admin/orders/{id:guid}/refund",
                static () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}
