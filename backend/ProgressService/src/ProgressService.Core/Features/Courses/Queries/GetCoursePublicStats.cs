using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Responses;

namespace ProgressService.Core.Features.Courses.Queries;

public sealed record GetCoursePublicStatsQuery(Guid CourseId) : IQuery;

public sealed class GetCoursePublicStatsQueryValidator : AbstractValidator<GetCoursePublicStatsQuery>
{
    public GetCoursePublicStatsQueryValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetCoursePublicStatsQuery.CourseId)));
    }
}

public sealed class GetCoursePublicStatsEndpoint : IEndpoint
{
    public const string ANONYMOUS_READ_RATE_LIMIT_POLICY = "anonymous-read";

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/courses/{courseId:guid}/public-stats",
                async Task<EndpointResult<CoursePublicStatsResponse>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCoursePublicStatsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCoursePublicStatsQuery(courseId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(ANONYMOUS_READ_RATE_LIMIT_POLICY);
}

public sealed class GetCoursePublicStatsHandler
    : IQueryHandlerWithResult<CoursePublicStatsResponse, GetCoursePublicStatsQuery>
{
    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(15),
    };

    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetCoursePublicStatsQuery> _validator;
    private readonly HybridCache _cache;
    private readonly IEducationContentServiceClient _educationContentServiceClient;

    public GetCoursePublicStatsHandler(
        ITransactionManager transactionManager,
        IValidator<GetCoursePublicStatsQuery> validator,
        HybridCache cache,
        IEducationContentServiceClient educationContentServiceClient)
    {
        _transactionManager = transactionManager;
        _validator = validator;
        _cache = cache;
        _educationContentServiceClient = educationContentServiceClient;
    }

    public async Task<Result<CoursePublicStatsResponse, Error>> Handle(
        GetCoursePublicStatsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Blueprint + SQL выполняются только на cache miss. На ECS-фейле фабрика возвращает
        // «гладкую деградацию» (нули) — это AllowAnonymousEndpoint, для публичных счётчиков
        // кратковременные нули приемлемы (cache TTL 60s). Ошибка логируется, следующий
        // запрос после expiry попробует ECS снова.
        // v5 (epic access-derive-model, Phase 1 / owner decision 2): публичный «N учеников» =
        // реально вовлечённые ученики (есть прогресс/активность по курсу), а НЕ держатели
        // grant'а и не eager-материализованные «призрачные» enrollment-строки. Под lazy-моделью
        // прогресс-строка ≈ вовлечённый юзер; но в backward-compat-окне eager-rows ещё живут,
        // поэтому считаем distinct юзеров с РЕАЛЬНЫМ сигналом: completed material_view ∩ курса,
        // либо issue/module progress по enrollment'у курса. viewed считается через
        // material_views ∩ blueprint.MaterialIds (модули + Лента + опубликованные подборки).
        string cacheKey = $"course-public-stats:{query.CourseId}:v5";

        CachedCoursePublicStats cached = await _cache.GetOrCreateAsync(
            cacheKey,
            async ct =>
            {
                Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult =
                    await _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                        new GetCourseProgressBlueprintsRequest([query.CourseId]),
                        ct);

                if (blueprintResult.IsFailure)
                {
                    return new CachedCoursePublicStats(0, 0, 0);
                }

                CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
                int totalMaterials = blueprint?.TotalMaterials ?? 0;
                Guid[] materialIds = blueprint is null
                    ? []
                    : (blueprint.MaterialIds as Guid[] ?? blueprint.MaterialIds.ToArray());

                DbConnection connection = _transactionManager.GetDbConnection();

                const string sql = """
                                   WITH engaged_users AS (
                                       -- Вовлечённый ученик = РЕАЛЬНАЯ активность по курсу:
                                       --   (a) completed material_view ∩ материалов курса, либо
                                       --   (b) issue/module progress по enrollment'у курса.
                                       -- Eager-материализованные «призрачные» enrollment-строки
                                       -- без активности сюда НЕ попадают (owner decision 2).
                                       SELECT DISTINCT mv.user_id
                                       FROM material_views mv
                                       WHERE mv.material_id = ANY(@MaterialIds)
                                         AND mv.is_completed = TRUE
                                       UNION
                                       SELECT DISTINCT ce.user_id
                                       FROM course_enrollments ce
                                       WHERE ce.course_id = @CourseId
                                         AND (
                                             EXISTS (SELECT 1 FROM issue_progress ip WHERE ip.enrollment_id = ce.id)
                                          OR EXISTS (SELECT 1 FROM module_progress mp WHERE mp.enrollment_id = ce.id)
                                         )
                                   ),
                                   per_user_viewed AS (
                                       -- is_completed=TRUE — только явно «Изучено» (issue #285).
                                       -- Silent track-view'ы НЕ должны раздувать «прошли курс» статистику.
                                       SELECT
                                           eu.user_id,
                                           COUNT(mv.id) AS viewed
                                       FROM engaged_users eu
                                       LEFT JOIN material_views mv
                                           ON mv.user_id = eu.user_id
                                          AND mv.material_id = ANY(@MaterialIds)
                                          AND mv.is_completed = TRUE
                                       GROUP BY eu.user_id
                                   )
                                   SELECT
                                       (SELECT COUNT(*) FROM engaged_users) AS enrolled_students_count,
                                       (SELECT COUNT(*) FROM per_user_viewed
                                            WHERE @TotalMaterials > 0
                                              AND viewed::float / @TotalMaterials >= 0.8) AS completed_students_count,
                                       COALESCE((SELECT AVG(
                                            CASE WHEN @TotalMaterials > 0
                                                THEN LEAST(viewed::float / @TotalMaterials * 100, 100)
                                                ELSE 0
                                            END
                                       ) FROM per_user_viewed), 0) AS average_progress_percent
                                   """;

                var row = await connection.QuerySingleAsync<StatsRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            query.CourseId,
                            TotalMaterials = totalMaterials,
                            MaterialIds = materialIds,
                        },
                        cancellationToken: ct));

                return new CachedCoursePublicStats(
                    row.EnrolledStudentsCount,
                    row.CompletedStudentsCount,
                    row.AverageProgressPercent);
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        return new CoursePublicStatsResponse(
            cached.EnrolledStudentsCount,
            cached.CompletedStudentsCount,
            cached.AverageProgressPercent);
    }

    private sealed record CachedCoursePublicStats(
        long EnrolledStudentsCount,
        long CompletedStudentsCount,
        double AverageProgressPercent);

    private sealed class StatsRow
    {
        public long EnrolledStudentsCount { get; init; }
        public long CompletedStudentsCount { get; init; }
        public double AverageProgressPercent { get; init; }
    }
}
