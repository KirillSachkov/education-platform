using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Responses;
using ProgressService.Domain;

namespace ProgressService.Core.Features.QuizAttempts.Admin;

public sealed record GetQuizAdminOverviewQuery : IQuery;

public sealed class GetQuizAdminOverviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/quizzes/admin/overview",
                async Task<EndpointResult<QuizAdminOverviewResponse>> (
                    [FromServices] GetQuizAdminOverviewHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetQuizAdminOverviewQuery(), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

/// <summary>
///     Админ-аналитика по всем тестам (#556, AC5): агрегат <c>quiz_attempts</c> по
///     <c>quiz_id</c> (число попыток, уникальные юзеры, доля зачётов, средний балл),
///     обогащённый title/курсом через ECS <see cref="IEducationContentServiceClient.GetQuizSummariesAsync"/>
///     + <see cref="IEducationContentServiceClient.GetCourseTitlesAsync"/>. LEVEL_TEST-квизы
///     исключаются (у воронки своя страница, #537), как и квизы без ECS-summary
///     (hard-deleted). Top-line KPI считаются по тем же отфильтрованным строкам.
///     Группировку по курсам делает фронт. Read-only Dapper; Tier-1 Users.VIEW.
/// </summary>
public sealed class GetQuizAdminOverviewHandler
    : IQueryHandlerWithResult<QuizAdminOverviewResponse, GetQuizAdminOverviewQuery>
{
    private const string LEVEL_TEST = "LEVEL_TEST";

    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;

    public GetQuizAdminOverviewHandler(
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient)
    {
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
    }

    public async Task<Result<QuizAdminOverviewResponse, Error>> Handle(
        GetQuizAdminOverviewQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                qa.quiz_id AS QuizId,
                CAST(COUNT(*) AS bigint) AS AttemptsCount,
                CAST(COUNT(DISTINCT qa.user_id) AS bigint) AS UniqueUsers,
                CAST(COUNT(*) FILTER (WHERE qa.passed) AS bigint) AS PassedCount,
                CAST(SUM(qa.score_percent) AS bigint) AS ScoreSum
            FROM quiz_attempts qa
            GROUP BY qa.quiz_id;
            """;

        IEnumerable<AggregateRow> rows = await connection.QueryAsync<AggregateRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        List<AggregateRow> aggregates = rows.ToList();

        if (aggregates.Count == 0)
        {
            return new QuizAdminOverviewResponse(
                TotalQuizzes: 0,
                TotalAttempts: 0,
                OverallPassRatePercent: 0,
                OverallAvgScorePercent: 0,
                Quizzes: []);
        }

        Guid[] quizIds = aggregates.Select(a => a.QuizId).ToArray();

        // ECS summaries-эндпоинт капается на 200 id'ов — чанкуем, иначе при >200 квизах
        // с попытками enrichment падает с 503 (#556 review).
        const int batchSize = 200;
        var summaryList = new List<QuizSummaryLookupDto>(quizIds.Length);
        foreach (Guid[] batch in quizIds.Chunk(batchSize))
        {
            Result<IReadOnlyList<QuizSummaryLookupDto>, Error> batchResult =
                await _educationContentServiceClient.GetQuizSummariesAsync(batch, cancellationToken);
            if (batchResult.IsFailure)
            {
                return ProgressErrors.EducationContentServiceUnavailable();
            }

            summaryList.AddRange(batchResult.Value);
        }

        Dictionary<Guid, QuizSummaryLookupDto> summaries = summaryList
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First());

        // Оставляем только MATERIAL_CHECK-квизы с известным summary.
        List<AggregateRow> kept = aggregates
            .Where(a => summaries.TryGetValue(a.QuizId, out QuizSummaryLookupDto? s)
                && !string.Equals(s.Purpose, LEVEL_TEST, StringComparison.Ordinal))
            .ToList();

        if (kept.Count == 0)
        {
            return new QuizAdminOverviewResponse(
                TotalQuizzes: 0,
                TotalAttempts: 0,
                OverallPassRatePercent: 0,
                OverallAvgScorePercent: 0,
                Quizzes: []);
        }

        Guid[] courseIds = kept
            .Select(a => summaries[a.QuizId].CourseId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        Dictionary<Guid, string> courseTitles = [];
        if (courseIds.Length > 0)
        {
            Result<IReadOnlyList<CourseTitleDto>, Error> titlesResult =
                await _educationContentServiceClient.GetCourseTitlesAsync(courseIds, cancellationToken);
            if (titlesResult.IsFailure)
            {
                return ProgressErrors.EducationContentServiceUnavailable();
            }

            courseTitles = titlesResult.Value
                .GroupBy(t => t.CourseId)
                .ToDictionary(g => g.Key, g => g.First().Title);
        }

        List<QuizAdminOverviewRow> quizzes = kept
            .Select(a =>
            {
                QuizSummaryLookupDto summary = summaries[a.QuizId];
                string? courseTitle = summary.CourseId is { } cid && courseTitles.TryGetValue(cid, out string? t)
                    ? t
                    : null;

                return new QuizAdminOverviewRow(
                    a.QuizId,
                    summary.Title,
                    summary.CourseId,
                    courseTitle,
                    a.AttemptsCount,
                    a.UniqueUsers,
                    a.AttemptsCount == 0
                        ? 0
                        : Math.Round(100.0 * a.PassedCount / a.AttemptsCount, 1),
                    a.AttemptsCount == 0
                        ? 0
                        : Math.Round((double)a.ScoreSum / a.AttemptsCount, 1));
            })
            .OrderByDescending(q => q.AttemptsCount)
            .ThenBy(q => q.Title, StringComparer.Ordinal)
            .ToList();

        long totalAttempts = kept.Sum(a => a.AttemptsCount);
        long totalPassed = kept.Sum(a => a.PassedCount);
        long totalScore = kept.Sum(a => a.ScoreSum);

        double overallPassRate = totalAttempts == 0
            ? 0
            : Math.Round(100.0 * totalPassed / totalAttempts, 1);
        double overallAvgScore = totalAttempts == 0
            ? 0
            : Math.Round((double)totalScore / totalAttempts, 1);

        return new QuizAdminOverviewResponse(
            quizzes.Count,
            totalAttempts,
            overallPassRate,
            overallAvgScore,
            quizzes);
    }

    private sealed class AggregateRow
    {
        public Guid QuizId { get; init; }

        public long AttemptsCount { get; init; }

        public long UniqueUsers { get; init; }

        public long PassedCount { get; init; }

        public long ScoreSum { get; init; }
    }
}
