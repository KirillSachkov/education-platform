using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;

namespace ProgressService.Core.Features.Materials.Queries;

/// <summary>
///     Batch-запрос на счётчики просмотров материалов (auth + anon, объединённые). Используется
///     ECS для бейджа «N просмотров» на карточках/детали материала. Анонимный эндпоинт — счётчик
///     публичный.
/// </summary>
public sealed record GetMaterialViewsCountsQuery(IReadOnlyCollection<Guid> MaterialIds) : IQuery;

public sealed class GetMaterialViewsCountsQueryValidator : AbstractValidator<GetMaterialViewsCountsQuery>
{
    /// <summary>
    ///     Безопасный потолок на размер batch'а: 256 материалов — больше, чем у любого
    ///     осмысленного UI-вьюпорта (фид/модуль/подборка). Защищает от запроса с десятком тысяч
    ///     id'шников через анонимный эндпоинт.
    /// </summary>
    public const int MAX_BATCH_SIZE = 256;

    public GetMaterialViewsCountsQueryValidator()
    {
        RuleFor(x => x.MaterialIds)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetMaterialViewsCountsQuery.MaterialIds)));

        RuleFor(x => x.MaterialIds)
            .Must(x => x.Count <= MAX_BATCH_SIZE)
            .When(x => x.MaterialIds is { Count: > 0 })
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetMaterialViewsCountsQuery.MaterialIds)));
    }
}

public sealed class GetMaterialViewsCountsEndpoint : IEndpoint
{
    public const string RATE_LIMIT_POLICY = "material-views-counts-read";

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/progress/materials/views/counts",
                async Task<EndpointResult<GetMaterialViewsCountsResponse>> (
                    [FromBody] GetMaterialViewsCountsRequest request,
                    [FromServices] GetMaterialViewsCountsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetMaterialViewsCountsQuery(request.MaterialIds),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(RATE_LIMIT_POLICY);
}

public sealed class GetMaterialViewsCountsHandler
    : IQueryHandlerWithResult<GetMaterialViewsCountsResponse, GetMaterialViewsCountsQuery>
{
    private readonly IValidator<GetMaterialViewsCountsQuery> _validator;
    private readonly IAnonymousMaterialViewRepository _repository;

    public GetMaterialViewsCountsHandler(
        IValidator<GetMaterialViewsCountsQuery> validator,
        IAnonymousMaterialViewRepository repository)
    {
        _validator = validator;
        _repository = repository;
    }

    public async Task<Result<GetMaterialViewsCountsResponse, Error>> Handle(
        GetMaterialViewsCountsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Дедупликация — несколько вызовов с одинаковым id в batch'е считаем один раз.
        Guid[] uniqueIds = query.MaterialIds.Distinct().ToArray();

        IReadOnlyDictionary<Guid, long> counts =
            await _repository.GetTotalViewsCountsAsync(uniqueIds, cancellationToken);

        // Возвращаем только те материалы, для которых в БД есть хотя бы один просмотр.
        // ECS / фронт трактуют отсутствующий id как 0 — экономим payload.
        List<MaterialViewsCountDto> items = counts
            .Where(kv => kv.Value > 0)
            .Select(kv => new MaterialViewsCountDto(kv.Key, kv.Value))
            .ToList();

        return new GetMaterialViewsCountsResponse(items);
    }
}
