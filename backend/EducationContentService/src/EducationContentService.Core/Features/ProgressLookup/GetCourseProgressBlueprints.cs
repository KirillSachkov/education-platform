using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.ProgressLookup;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProgressLookup;

public sealed record GetCourseProgressBlueprintsQuery(IReadOnlyCollection<Guid> CourseIds) : IQuery;

public sealed class GetCourseProgressBlueprintsQueryValidator
    : AbstractValidator<GetCourseProgressBlueprintsQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetCourseProgressBlueprintsQueryValidator()
    {
        RuleFor(x => x.CourseIds)
            .NotNull()
            .WithMessage("Список курсов обязателен")
            .Must(ids => ids is null || ids.Count <= MAX_BATCH_SIZE)
            .WithMessage($"Количество курсов не может превышать {MAX_BATCH_SIZE}")
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty))
            .WithMessage("Идентификаторы курсов не могут быть пустыми")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Идентификаторы курсов не должны содержать дубликаты");
    }
}

public sealed class GetCourseProgressBlueprintsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/progress/courses/blueprints", async Task<EndpointResult<IReadOnlyCollection<CourseProgressBlueprintDto>>> (
            [FromBody] GetCourseProgressBlueprintsRequest request,
            [FromServices] GetCourseProgressBlueprintsHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetCourseProgressBlueprintsQuery(request.CourseIds), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetCourseProgressBlueprintsHandler
    : IQueryHandlerWithResult<IReadOnlyCollection<CourseProgressBlueprintDto>, GetCourseProgressBlueprintsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IValidator<GetCourseProgressBlueprintsQuery> _validator;
    private readonly ILogger<GetCourseProgressBlueprintsHandler> _logger;

    public GetCourseProgressBlueprintsHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        IValidator<GetCourseProgressBlueprintsQuery> validator,
        ILogger<GetCourseProgressBlueprintsHandler> logger)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>> Handle(
        GetCourseProgressBlueprintsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Guid[] courseIds = query.CourseIds.ToArray();

        if (courseIds.Length == 0)
        {
            return Result.Success<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>([]);
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // material_refs объединяет два источника материалов курса: course_materials (по INV-4
        // содержит ВСЕ материалы, привязанные к модулям курса + материалы из Ленты курса)
        // и collection_items опубликованных подборок курса. UNION дедуплицирует material_id —
        // материал, лежащий и в модуле, и в подборке, считается один раз.
        // Намеренные изменения семантики против предыдущей версии (#40):
        //   1. Фильтр `is_optional = FALSE` убран на обоих уровнях: материалы из опциональных
        //      модулей и из Ленты/подборок (где понятия optional нет) тоже учитываются — все
        //      материалы курса должны влиять на прогресс с точки зрения студента.
        //   2. (#496) Учитываются только PUBLISHED материалы и задачи: DRAFT/ARCHIVED-элементы
        //      невидимы студенту, и их попадание в знаменатели делало 100% и сертификат
        //      недостижимыми, а счётчики сайдбара расходились со страницей программы.
        // quiz_refs (ST-13 #493): квизы — только из module_items(item_type='Quiz') модулей курса
        // (прогресс считается по элементам программы; course_quizzes без module_item — привязка
        // без позиции в программе) и только PUBLISHED (DRAFT-квиз студент пройти не может —
        // он не должен блокировать 100%/сертификат). is_optional не фильтруется — зеркало
        // material_refs (см. п.1 выше).
        const string sql = """
                           WITH requested_courses AS (
                               SELECT DISTINCT UNNEST(@CourseIds::uuid[]) AS course_id
                           ),
                           module_totals AS (
                               SELECT
                                   ci.course_id,
                                   COUNT(*)::integer AS total_modules
                               FROM requested_courses rc
                               JOIN course_items ci
                                   ON ci.course_id = rc.course_id
                                  AND ci.item_type = 'Module'
                                  AND ci.is_optional = FALSE
                               GROUP BY ci.course_id
                           ),
                           material_refs AS (
                               SELECT cm.course_id, cm.material_id
                               FROM requested_courses rc
                               JOIN course_materials cm
                                   ON cm.course_id = rc.course_id
                               JOIN materials m
                                   ON m.id = cm.material_id
                                  AND m.status = 'PUBLISHED'

                               UNION

                               SELECT col.course_id, ci.reference_id AS material_id
                               FROM requested_courses rc
                               JOIN collections col
                                   ON col.course_id = rc.course_id
                                  AND col.status = 'PUBLISHED'
                               JOIN collection_sections cs
                                   ON cs.collection_id = col.id
                               JOIN collection_items ci
                                   ON ci.section_id = cs.id
                                  AND ci.item_type = 'MATERIAL'
                               JOIN materials cim
                                   ON cim.id = ci.reference_id
                                  AND cim.status = 'PUBLISHED'
                           ),
                           material_totals AS (
                               SELECT
                                   course_id,
                                   COUNT(*)::integer AS total_materials,
                                   ARRAY_AGG(material_id ORDER BY material_id) AS material_ids
                               FROM material_refs
                               GROUP BY course_id
                           ),
                           issue_refs AS (
                               SELECT
                                   ci.course_id,
                                   pi.issue_id
                               FROM requested_courses rc
                               JOIN course_items ci
                                   ON ci.course_id = rc.course_id
                                  AND ci.item_type = 'Project'
                                  AND ci.is_optional = FALSE
                               JOIN project_items pi
                                   ON pi.project_id = ci.reference_id
                                  AND pi.is_optional = FALSE
                               JOIN issues pis
                                   ON pis.id = pi.issue_id
                                  AND pis.status = 'PUBLISHED'

                               UNION

                               SELECT
                                   ci.course_id,
                                   mi.reference_id AS issue_id
                               FROM requested_courses rc
                               JOIN course_items ci
                                   ON ci.course_id = rc.course_id
                                  AND ci.item_type = 'Module'
                                  AND ci.is_optional = FALSE
                               JOIN module_items mi
                                   ON mi.module_id = ci.reference_id
                                  AND mi.item_type = 'Issue'
                                  AND mi.is_optional = FALSE
                               JOIN issues mis
                                   ON mis.id = mi.reference_id
                                  AND mis.status = 'PUBLISHED'
                           ),
                           issue_totals AS (
                               SELECT
                                   ir.course_id,
                                   COUNT(DISTINCT ir.issue_id)::integer AS total_unique_issues
                               FROM issue_refs ir
                               GROUP BY ir.course_id
                           ),
                           quiz_refs AS (
                               SELECT DISTINCT
                                   ci.course_id,
                                   mi.reference_id AS quiz_id
                               FROM requested_courses rc
                               JOIN course_items ci
                                   ON ci.course_id = rc.course_id
                                  AND ci.item_type = 'Module'
                               JOIN module_items mi
                                   ON mi.module_id = ci.reference_id
                                  AND mi.item_type = 'Quiz'
                               JOIN quizzes q
                                   ON q.id = mi.reference_id
                                  AND q.status = 'PUBLISHED'
                           ),
                           quiz_totals AS (
                               SELECT
                                   course_id,
                                   COUNT(*)::integer AS total_quizzes,
                                   ARRAY_AGG(quiz_id ORDER BY quiz_id) AS quiz_ids
                               FROM quiz_refs
                               GROUP BY course_id
                           )
                           SELECT
                               c.id AS course_id,
                               c.slug AS course_slug,
                               c.title AS title,
                               c.description AS description,
                               c.image_id AS image_id,
                               NULL::text AS image_url,
                               COALESCE(mt.total_modules, 0) AS total_modules,
                               COALESCE(mat.total_materials, 0) AS total_materials,
                               COALESCE(mat.material_ids, ARRAY[]::uuid[]) AS material_ids,
                               COALESCE(it.total_unique_issues, 0) AS total_unique_issues,
                               COALESCE(qt.total_quizzes, 0) AS total_quizzes,
                               COALESCE(qt.quiz_ids, ARRAY[]::uuid[]) AS quiz_ids,
                               COALESCE(mat.total_materials, 0)
                                 + COALESCE(it.total_unique_issues, 0)
                                 + COALESCE(qt.total_quizzes, 0) AS total_items,
                               c.is_new AS is_new,
                               c.sort_key AS sort_key,
                               c.kind AS kind
                           FROM requested_courses rc
                           JOIN courses c
                               ON c.id = rc.course_id
                           LEFT JOIN module_totals mt
                               ON mt.course_id = c.id
                           LEFT JOIN material_totals mat
                               ON mat.course_id = c.id
                           LEFT JOIN issue_totals it
                               ON it.course_id = c.id
                           LEFT JOIN quiz_totals qt
                               ON qt.course_id = c.id
                           ORDER BY c.sort_key ASC, c.id ASC;
                           """;

        List<CourseProgressBlueprintRow> rows = (await connection.QueryAsync<CourseProgressBlueprintRow>(
            sql,
            new { CourseIds = courseIds }))
            .ToList();

        List<Guid> imageIds = rows
            .Where(r => r.ImageId is not null)
            .Select(r => r.ImageId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> imageUrlMap = [];

        if (imageIds.Count > 0)
        {
            var batchResult = await _fileServiceClient.GetFilesBatchAsync(imageIds, cancellationToken);
            if (batchResult is { IsSuccess: true, Value: not null })
            {
                foreach (GetFileResponse file in batchResult.Value)
                {
                    if (file.ContentUrl is not null)
                        imageUrlMap[file.Id] = file.ContentUrl;
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch batch images for course progress blueprints");
            }
        }

        List<CourseProgressBlueprintDto> result = rows
            .Select(row =>
            {
                string? imageUrl = row.ImageId is not null && imageUrlMap.TryGetValue(row.ImageId.Value, out string? url)
                    ? url
                    : null;

                return new CourseProgressBlueprintDto(
                    row.CourseId, row.CourseSlug, row.Title, row.Description, row.ImageId, imageUrl,
                    row.TotalModules, row.TotalMaterials, row.MaterialIds ?? [],
                    row.TotalUniqueIssues, row.TotalQuizzes, row.QuizIds ?? [],
                    row.TotalItems, row.IsNew, row.SortKey, row.Kind);
            })
            .ToList();

        return Result.Success<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>(result);
    }

    private sealed class CourseProgressBlueprintRow
    {
        public Guid CourseId { get; init; }
        public string CourseSlug { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Description { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public string? ImageUrl { get; init; }
        public int TotalModules { get; init; }
        public int TotalMaterials { get; init; }
        public Guid[]? MaterialIds { get; init; }
        public int TotalUniqueIssues { get; init; }
        public int TotalQuizzes { get; init; }
        public Guid[]? QuizIds { get; init; }
        public int TotalItems { get; init; }
        public bool IsNew { get; init; }
        public string SortKey { get; init; } = null!;
        public string Kind { get; init; } = null!;
    }
}
