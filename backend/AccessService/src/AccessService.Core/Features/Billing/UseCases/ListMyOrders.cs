using AccessService.Contracts.Billing;
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
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>
/// GET /access/me/orders/ — owner-only список заказов текущего юзера. Используется
/// фронтом на странице «Мои платежи» (`/settings/payments`). Сортировка по
/// <c>created_at DESC</c>; `Provider`/`ExternalProviderRef` не возвращаются —
/// юзеру не нужны внутренние ссылки провайдера.
/// </summary>
public sealed class ListMyOrdersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/access/me/orders/", async Task<EndpointResult<ListMyOrdersResponse>> (
                [FromQuery] string? status,
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromServices] ListMyOrdersHandler handler,
                CancellationToken ct) =>
                await handler.Handle(
                    new ListMyOrdersQuery(status, page ?? 1, pageSize ?? 20),
                    ct))
            .RequireAuthorization();
}

public sealed record ListMyOrdersQuery(string? Status, int Page, int PageSize) : IQuery;

public sealed class ListMyOrdersQueryValidator : AbstractValidator<ListMyOrdersQuery>
{
    public ListMyOrdersQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithError(Error.Validation("order.list.page.invalid", "page должно быть >= 1"));
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50)
            .WithError(Error.Validation("order.list.pageSize.invalid",
                "pageSize должно быть от 1 до 50"));
        RuleFor(x => x.Status)
            .Must(s => s == null || Enum.TryParse<OrderStatus>(s, ignoreCase: false, out _))
            .WithError(Error.Validation("order.list.status.invalid",
                "Status должен быть одним из: PENDING/PAID/FAILED/REFUNDED"));
    }
}

public sealed class ListMyOrdersHandler
    : IQueryHandlerWithResult<ListMyOrdersResponse, ListMyOrdersQuery>
{
    private readonly IOrdersRepository _orders;
    private readonly IPlansRepository _plans;
    private readonly UserScopedData _user;
    private readonly IValidator<ListMyOrdersQuery> _validator;

    public ListMyOrdersHandler(
        IOrdersRepository orders,
        IPlansRepository plans,
        UserScopedData user,
        IValidator<ListMyOrdersQuery> validator)
    {
        _orders = orders;
        _plans = plans;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<ListMyOrdersResponse, Error>> Handle(
        ListMyOrdersQuery query,
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

        // userId hardcoded в _user.UserId — owner-scoped. Server-verified, не из request.
        (IReadOnlyList<Order> items, int total) = await _orders.SearchAsync(
            statusFilter,
            userId: _user.UserId,
            planId: null,
            createdFrom: null,
            createdTo: null,
            correlationId: null,
            query.Page,
            query.PageSize,
            cancellationToken);

        // #512 — название плана отдаём прямо в заказе, чтобы фронт (/payments)
        // не тянул весь публичный каталог планов ради display name'ов.
        // Планы в той же БД сервиса — один batch-запрос по distinct PlanId.
        List<Guid> planIds = items.Select(o => o.PlanId).Distinct().ToList();
        Dictionary<Guid, string> planTitleById = planIds.Count > 0
            ? (await _plans.GetManyByAsync(p => planIds.Contains(p.Id), cancellationToken))
                .ToDictionary(p => p.Id, p => p.DisplayName.Value)
            : [];

        IReadOnlyList<MeOrderSummary> summaries = items
            .Select(o => new MeOrderSummary(
                o.Id,
                o.PlanId,
                o.AmountCents,
                o.Currency,
                o.Status.ToString(),
                o.CreatedAt,
                o.PaidAt,
                // #414 — не утекать сырой internal FailureReason (errorCode'ы, amount-mismatch
                // детали) владельцу заказа; тот же бакетинг, что в GetOrderStatus.
                FailureReasonNormalizer.ToUserFacing(o.FailureReason),
                planTitleById.GetValueOrDefault(o.PlanId)))
            .ToList();

        return new ListMyOrdersResponse(summaries, total, query.Page, query.PageSize);
    }
}
