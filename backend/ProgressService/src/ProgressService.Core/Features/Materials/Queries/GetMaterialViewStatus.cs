using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;

namespace ProgressService.Core.Features.Materials.Queries;

public sealed record GetMaterialViewStatusQuery(IReadOnlyCollection<Guid> MaterialIds) : IQuery;

public sealed class GetMaterialViewStatusQueryValidator : AbstractValidator<GetMaterialViewStatusQuery>
{
    public GetMaterialViewStatusQueryValidator()
    {
        RuleFor(x => x.MaterialIds)
            .NotEmpty()
            .Must(items => items.Count <= 200)
            .WithError(GeneralErrors.ValueIsInvalid("materialIds"));
    }
}

public sealed class GetMaterialViewStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/materials/view-status",
                async Task<EndpointResult<GetMaterialViewStatusResponse>> (
                    [FromBody] GetMaterialViewStatusRequest request,
                    [FromServices] GetMaterialViewStatusHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetMaterialViewStatusQuery(request.MaterialIds),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Батч-lookup: возвращает для текущего пользователя статус «изучено» по списку материалов.
///     Материал считается изученным, если у пользователя есть запись в <c>material_views</c>
///     с <c>is_completed=true</c> (т.е. он явно нажал «Отметить изученным», issue #285).
///     Silent track-view'ы (mount detail-страницы) сюда НЕ попадают — они питают только
///     счётчик «N просмотров» (#234). Материалы без completed-записи возвращаются как
///     <c>isViewed=false</c> — в ответе всегда присутствуют все запрошенные IDs, чтобы
///     фронт мог merge 1-к-1.
/// </summary>
public sealed class GetMaterialViewStatusHandler
    : IQueryHandlerWithResult<GetMaterialViewStatusResponse, GetMaterialViewStatusQuery>
{
    private readonly IValidator<GetMaterialViewStatusQuery> _validator;
    private readonly IMaterialViewRepository _materialViewRepository;
    private readonly UserScopedData _user;

    public GetMaterialViewStatusHandler(
        IValidator<GetMaterialViewStatusQuery> validator,
        IMaterialViewRepository materialViewRepository,
        UserScopedData user)
    {
        _validator = validator;
        _materialViewRepository = materialViewRepository;
        _user = user;
    }

    public async Task<Result<GetMaterialViewStatusResponse, Error>> Handle(
        GetMaterialViewStatusQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Guid[] materialIds = query.MaterialIds.Distinct().ToArray();

        IReadOnlyDictionary<Guid, DateTime> viewedMap = await _materialViewRepository
            .GetCompletedMaterialMapAsync(_user.UserId, materialIds, cancellationToken);

        MaterialViewStatusDto[] items = materialIds
            .Select(id =>
            {
                bool isViewed = viewedMap.TryGetValue(id, out DateTime viewedAt);
                return new MaterialViewStatusDto(id, isViewed, isViewed ? viewedAt : null);
            })
            .ToArray();

        return new GetMaterialViewStatusResponse(items);
    }
}
