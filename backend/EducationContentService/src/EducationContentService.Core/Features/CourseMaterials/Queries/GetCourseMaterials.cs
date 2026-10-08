using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Materials;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.CourseMaterials.Queries;

public sealed record GetCourseMaterialsQuery(
    Guid CourseId,
    string? Cursor,
    int Limit,
    string? Kind,
    string? Search) : IQuery;

public sealed class GetCourseMaterialsQueryValidator : AbstractValidator<GetCourseMaterialsQuery>
{
    public GetCourseMaterialsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetCourseMaterialsQuery.Limit)));
    }
}

public sealed class GetCourseMaterialsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/materials",
                async Task<EndpointResult<CursorResponse<MaterialSummaryDto>>> (
                    [FromRoute] Guid courseId,
                    [FromQuery] string? cursor,
                    [FromQuery] int? limit,
                    [FromQuery] string? kind,
                    [FromQuery] string? search,
                    [FromServices] GetCourseMaterialsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetCourseMaterialsQuery(
                        courseId,
                        cursor,
                        limit is null or 0 ? 20 : limit.Value,
                        kind,
                        search),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetCourseMaterialsHandler
    : IQueryHandlerWithResult<CursorResponse<MaterialSummaryDto>, GetCourseMaterialsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetCourseMaterialsQuery> _validator;
    private readonly UserScopedData _userData;

    public GetCourseMaterialsHandler(
        ITransactionManager transactionManager,
        IValidator<GetCourseMaterialsQuery> validator,
        UserScopedData userData)
    {
        _transactionManager = transactionManager;
        _validator = validator;
        _userData = userData;
    }

    public async Task<Result<CursorResponse<MaterialSummaryDto>, Error>> Handle(
        GetCourseMaterialsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Cursor? cursor = Cursor.Decode(query.Cursor);

        DbConnection connection = _transactionManager.GetDbConnection();

        Guid userId = _userData.IsAuthenticated ? _userData.UserId : Guid.Empty;

        string? searchPattern = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : $"%{query.Search.Trim().ToLowerInvariant()}%";

        var parameters = new
        {
            query.CourseId,
            CursorCreatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = query.Limit + 1,
            UserId = userId,
            KindFilter = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind,
            SearchPattern = searchPattern
        };

        // Материалы курса = только course_materials (см. MATERIAL_LIFECYCLE.md концепт «привязки»).
        // Подборки — отдельная сущность, их материалы не протекают в список курсовых.
        // Инвариант INV-4 гарантирует, что материалы из module_items курса тоже есть в course_materials.
        //
        // Видимость: PUBLISHED — все, плюс автор курса видит свои DRAFT/ARCHIVED (материалы,
        // которые ему же принадлежат и которые он только что добавил/не успел опубликовать).
        //
        // Фильтры (опциональны, параметр-null → no-op):
        //   - KindFilter — точное совпадение по m.kind (uppercase enum name).
        //   - SearchPattern — ILIKE '%search%' по title (без учёта регистра).
        // `lower(m.title) LIKE @SearchPattern` — через GIN-индекс
        // `ix_materials_title_trgm ON materials USING gin (lower(title) gin_trgm_ops)`.
        // SearchPattern уже приводится к lower() на стороне C# (см. выше).
        const string countSql = """
                                SELECT COUNT(*)
                                FROM course_materials cm
                                JOIN materials m ON m.id = cm.material_id
                                JOIN courses c ON c.id = cm.course_id
                                WHERE cm.course_id = @CourseId
                                  AND (m.status = 'PUBLISHED' OR c.author_id = @UserId)
                                  AND (@KindFilter IS NULL OR m.kind = @KindFilter)
                                  AND (@SearchPattern IS NULL OR lower(m.title) LIKE @SearchPattern);
                                """;

        const string dataSql = """
                               SELECT
                                   m.id,
                                   m.author_id,
                                   m.title,
                                   CASE
                                       WHEN m.access_type = 'PUBLIC' OR c.author_id = @UserId THEN LEFT(m.content, 280)
                                       ELSE NULL
                                   END AS preview,
                                   m.kind,
                                   m.status,
                                   m.access_type,
                                   m.created_at,
                                   m.updated_at,
                                   m.published_at,
                                   m.image_id,
                                   m.video_id,
                                   m.quiz_id
                               FROM course_materials cm
                               JOIN materials m ON m.id = cm.material_id
                               JOIN courses c ON c.id = cm.course_id
                               WHERE cm.course_id = @CourseId
                                 AND (m.status = 'PUBLISHED' OR c.author_id = @UserId)
                                 AND (@KindFilter IS NULL OR m.kind = @KindFilter)
                                 AND (@SearchPattern IS NULL OR lower(m.title) LIKE @SearchPattern)
                                 AND (@CursorCreatedAt IS NULL OR (m.created_at, m.id) < (@CursorCreatedAt, @CursorId))
                               ORDER BY m.created_at DESC, m.id DESC
                               LIMIT @Limit;
                               """;

        var countCommand = new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken);
        long totalCount = await connection.ExecuteScalarAsync<long>(countCommand);

        var dataCommand = new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken);
        List<MaterialSummaryDto> rows = (await connection.QueryAsync<MaterialSummaryDto>(dataCommand)).ToList();

        bool hasMore = rows.Count > query.Limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        string? nextCursor = hasMore
            ? Cursor.Encode(rows[^1].CreatedAt, rows[^1].Id)
            : null;

        return new CursorResponse<MaterialSummaryDto>
        {
            Items = rows,
            NextCursor = nextCursor,
            TotalCount = totalCount
        };
    }
}
