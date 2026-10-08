using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Digest;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Digest;

/// <summary>
///     Internal endpoint для еженедельного дайджеста (#532): опубликованные за окно
///     материалы (с primary-привязкой к PUBLISHED-курсу) и курсы. Отдаёт только
///     метаданные (title/slug) — промо-уровень, как каталог; тела материалов не утекают.
///     Consumer — NotificationService.WeeklyDigestRunner.
/// </summary>
public sealed record GetDigestContentQuery(DateTime SinceUtc, int MaxItemsPerKind) : IQuery;

public sealed class GetDigestContentQueryValidator : AbstractValidator<GetDigestContentQuery>
{
    public const int MAX_ITEMS_LIMIT = 50;

    public GetDigestContentQueryValidator()
    {
        RuleFor(x => x.SinceUtc)
            .NotEmpty();

        RuleFor(x => x.MaxItemsPerKind)
            .InclusiveBetween(1, MAX_ITEMS_LIMIT);
    }
}

public sealed class GetDigestContentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/digest/content",
                async Task<EndpointResult<DigestContentDto>> (
                    [FromQuery] DateTime sinceUtc,
                    [FromQuery] int? maxItems,
                    [FromServices] GetDigestContentHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetDigestContentQuery(
                            DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc),
                            maxItems ?? GetDigestContentHandler.DEFAULT_MAX_ITEMS),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetDigestContentHandler
    : IQueryHandlerWithResult<DigestContentDto, GetDigestContentQuery>
{
    public const int DEFAULT_MAX_ITEMS = 20;

    private const int QUERY_TIMEOUT_SECONDS = 30;

    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetDigestContentQuery> _validator;

    public GetDigestContentHandler(
        ITransactionManager transactionManager,
        IValidator<GetDigestContentQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<DigestContentDto, Error>> Handle(
        GetDigestContentQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Primary course binding — самая ранняя course_materials запись (id = Guid v7,
        // time-ordered), только PUBLISHED-курсы — та же идиома, что в GetMaterialCourseBindings.
        const string sql = """
                           SELECT m.id           AS MaterialId,
                                  m.title        AS Title,
                                  cb.course_slug  AS CourseSlug,
                                  cb.course_title AS CourseTitle,
                                  m.published_at AS PublishedAt
                           FROM materials m
                           LEFT JOIN LATERAL (
                               SELECT c.slug AS course_slug, c.title AS course_title
                               FROM course_materials cm
                               JOIN courses c ON c.id = cm.course_id AND c.status = 'PUBLISHED'
                               WHERE cm.material_id = m.id
                               ORDER BY cm.id
                               LIMIT 1
                           ) cb ON TRUE
                           WHERE m.status = 'PUBLISHED' AND m.published_at >= @SinceUtc
                           ORDER BY m.published_at DESC
                           LIMIT @MaxItems;

                           SELECT c.id           AS CourseId,
                                  c.title        AS Title,
                                  c.slug         AS Slug,
                                  c.kind         AS Kind,
                                  c.published_at AS PublishedAt
                           FROM courses c
                           WHERE c.status = 'PUBLISHED' AND c.published_at >= @SinceUtc
                           ORDER BY c.published_at DESC
                           LIMIT @MaxItems;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                new { query.SinceUtc, MaxItems = query.MaxItemsPerKind },
                commandTimeout: QUERY_TIMEOUT_SECONDS,
                cancellationToken: cancellationToken));

        IReadOnlyList<DigestMaterialDto> materials = (await grid.ReadAsync<DigestMaterialDto>()).ToList();
        IReadOnlyList<DigestCourseDto> courses = (await grid.ReadAsync<DigestCourseDto>()).ToList();

        return Result.Success<DigestContentDto, Error>(new DigestContentDto(materials, courses));
    }
}
