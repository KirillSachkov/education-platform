using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.Materials.Queries;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.HttpCommunication;

namespace EducationContentService.Core.Features.Materials.UseCases;

/// <summary>
///     Internal batch endpoint — отдаёт rich-summary материалов по списку ID
///     (id, author, title, preview, kind, status, access_type, image/video,
///     thumbnail, views). Используется AccessService для обогащения карточек
///     закреплённых (pinned) материалов на home-дашборде (epic #397).
///     S2S-only: не фильтрует по доступу — caller делает собственный entitlement-чек.
///     Markdown-тело материала не утекает (отдаём только preview = первые 280 символов).
/// </summary>
public sealed record GetMaterialSummariesQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetMaterialSummariesQueryValidator : AbstractValidator<GetMaterialSummariesQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetMaterialSummariesQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetMaterialSummariesQuery.Ids)))
            .Must(ids => ids is null || ids.Count <= MAX_BATCH_SIZE)
            .WithError(GeneralErrors.OutOfRange(nameof(GetMaterialSummariesQuery.Ids), min: 1, max: MAX_BATCH_SIZE));
    }
}

public sealed class GetMaterialSummariesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/materials/summaries",
                async Task<EndpointResult<IReadOnlyList<MaterialSummaryDto>>> (
                    [FromBody] GetMaterialSummariesRequest request,
                    [FromServices] GetMaterialSummariesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialSummariesQuery(request.Ids), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetMaterialSummariesHandler
    : IQueryHandlerWithResult<IReadOnlyList<MaterialSummaryDto>, GetMaterialSummariesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetMaterialSummariesQuery> _validator;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;

    public GetMaterialSummariesHandler(
        ITransactionManager transactionManager,
        IValidator<GetMaterialSummariesQuery> validator,
        IFileServiceClient fileServiceClient,
        IProgressServiceClient progressServiceClient)
    {
        _transactionManager = transactionManager;
        _validator = validator;
        _fileServiceClient = fileServiceClient;
        _progressServiceClient = progressServiceClient;
    }

    public async Task<Result<IReadOnlyList<MaterialSummaryDto>, Error>> Handle(
        GetMaterialSummariesQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        // Caller (AccessService) does its own entitlement check, поэтому здесь
        // возвращаем summary независимо от status/access_type. Тело материала
        // (markdown content) НЕ отдаём — только preview = первые 280 символов.
        const string sql = """
                           SELECT
                               id,
                               author_id,
                               title,
                               LEFT(content, 280) AS preview,
                               kind,
                               status,
                               access_type,
                               created_at,
                               updated_at,
                               published_at,
                               image_id,
                               video_id,
                               quiz_id
                           FROM materials
                           WHERE id = ANY(@Ids);
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        List<MaterialSummaryDto> materials = (await connection.QueryAsync<MaterialSummaryDto>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken))).ToList();

        materials = await MaterialFeedEnricher.EnrichSummaryThumbnailsAsync(
            materials, _fileServiceClient, _progressServiceClient, cancellationToken);

        return Result.Success<IReadOnlyList<MaterialSummaryDto>, Error>(materials);
    }
}
