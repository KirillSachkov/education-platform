using AccessService.Contracts.Billing.Admin;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Billing.UseCases.Admin;

/// <summary>
/// GET /access/admin/orders/ — admin/moderator-only поиск заказов с пагинацией.
/// Используется в admin-панели для расследования застрявших заказов и жалоб
/// клиентов «оплатил, но доступа нет».
///
/// Phase F.1.5 (issue #102).
/// </summary>
public sealed class ListAdminOrdersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/access/admin/orders/", async Task<EndpointResult<ListAdminOrdersResponse>> (
                [FromQuery] string? status,
                [FromQuery] Guid? userId,
                [FromQuery] Guid? planId,
                [FromQuery] DateTimeOffset? createdFrom,
                [FromQuery] DateTimeOffset? createdTo,
                [FromQuery] string? correlationId,
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromServices] ListAdminOrdersHandler handler,
                CancellationToken ct) =>
                await handler.Handle(
                    new ListAdminOrdersQuery(
                        status,
                        userId,
                        planId,
                        createdFrom,
                        createdTo,
                        correlationId,
                        page ?? 1,
                        pageSize ?? 20),
                    ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}

public sealed record ListAdminOrdersQuery(
    string? Status,
    Guid? UserId,
    Guid? PlanId,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    string? CorrelationId,
    int Page,
    int PageSize) : IQuery;

public sealed class ListAdminOrdersQueryValidator : AbstractValidator<ListAdminOrdersQuery>
{
    public ListAdminOrdersQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithError(Error.Validation("order.list.page.invalid", "page должно быть >= 1"));
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithError(Error.Validation("order.list.pageSize.invalid", "pageSize должно быть от 1 до 100"));
        RuleFor(x => x.Status)
            .Must(s => s == null || Enum.TryParse<OrderStatus>(s, ignoreCase: false, out _))
            .WithError(Error.Validation("order.list.status.invalid",
                "Status должен быть одним из: PENDING/PAID/FAILED/REFUNDED"));
    }
}

public sealed class ListAdminOrdersHandler
    : IQueryHandlerWithResult<ListAdminOrdersResponse, ListAdminOrdersQuery>
{
    private readonly IOrdersRepository _orders;
    private readonly IValidator<ListAdminOrdersQuery> _validator;

    public ListAdminOrdersHandler(
        IOrdersRepository orders,
        IValidator<ListAdminOrdersQuery> validator)
    {
        _orders = orders;
        _validator = validator;
    }

    public async Task<Result<ListAdminOrdersResponse, Error>> Handle(
        ListAdminOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        OrderStatus? statusFilter = null;
        if (query.Status is not null
            && Enum.TryParse<OrderStatus>(query.Status, ignoreCase: false, out OrderStatus parsed))
        {
            statusFilter = parsed;
        }

        (IReadOnlyList<Order> items, int total) = await _orders.SearchAsync(
            statusFilter,
            query.UserId,
            query.PlanId,
            query.CreatedFrom,
            query.CreatedTo,
            query.CorrelationId,
            query.Page,
            query.PageSize,
            cancellationToken);

        IReadOnlyList<AdminOrderSummary> summaries = items
            .Select(o => new AdminOrderSummary(
                o.Id,
                o.UserId,
                o.PlanId,
                o.AmountCents,
                o.Currency,
                o.Status.ToString(),
                o.Provider,
                o.ExternalProviderRef,
                o.CreatedAt,
                o.PaidAt,
                o.FailureReason,
                o.CorrelationId))
            .ToList();

        return new ListAdminOrdersResponse(summaries, total, query.Page, query.PageSize);
    }
}
