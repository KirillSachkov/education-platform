using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using EducationContentService.Contracts.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Domain;

namespace ProgressService.Core.Features.Courses.Stats;

public sealed record GetCourseQuizStatsOverviewQuery(Guid CourseId) : IQuery;

public sealed class GetCourseQuizStatsOverviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/courses/{courseId:guid}/stats/quizzes/",
                async Task<EndpointResult<CourseQuizStatsOverviewResponse>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseQuizStatsOverviewHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetCourseQuizStatsOverviewQuery(courseId), ct))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
}

/// <summary>Агрегирует попытки только по квизам указанного курса под проверкой ownership.
///     Курс без квизов возвращает пустой список.</summary>
public sealed class GetCourseQuizStatsOverviewHandler
    : IQueryHandlerWithResult<CourseQuizStatsOverviewResponse, GetCourseQuizStatsOverviewQuery>
{
    private const int SUMMARY_BATCH_SIZE = 200;

    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly UserScopedData _user;

    public GetCourseQuizStatsOverviewHandler(
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
        _user = user;
    }

    public async Task<Result<CourseQuizStatsOverviewResponse, Error>> Handle(
        GetCourseQuizStatsOverviewQuery query,
        CancellationToken cancellationToken)
    {
        Result<CourseDto, Error> authResult = await CourseStatsAuthorization.AuthorizeCourseManagementAsync(
            _educationContentServiceClient, _user, query.CourseId, cancellationToken);
        if (authResult.IsFailure)
        {
            return authResult.Error;
        }

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult =
            await _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest([query.CourseId]),
                cancellationToken);
        if (blueprintResult.IsFailure)
        {
            return ProgressErrors.EducationContentServiceUnavailable();
        }

        CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
        Guid[] quizIds = blueprint is null
            ? []
            : blueprint.QuizIds as Guid[] ?? blueprint.QuizIds.ToArray();

        if (quizIds.Length == 0)
        {
            return Empty(query.CourseId);
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // avgScore считается на стороне SQL (SUM(score_percent)/COUNT) — может на доли %
        // отличаться от drill-in GetQuizAdminStats (in-memory Average), это намеренно: не
        // материализуем все попытки. Зеркало GetQuizAdminOverview (score_percent — integer).
        const string sql = """
            SELECT
                qa.quiz_id AS QuizId,
                CAST(COUNT(*) AS bigint) AS AttemptsCount,
                CAST(COUNT(DISTINCT qa.user_id) AS bigint) AS UniqueUsers,
                CAST(COUNT(*) FILTER (WHERE qa.passed) AS bigint) AS PassedCount,
                CAST(SUM(qa.score_percent) AS bigint) AS ScoreSum
            FROM quiz_attempts qa
            WHERE qa.quiz_id = ANY(@QuizIds)
            GROUP BY qa.quiz_id;
            """;

        DynamicParameters parameters = new();
        parameters.Add("QuizIds", quizIds);

        IEnumerable<AggregateRow> rows = await connection.QueryAsync<AggregateRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        List<AggregateRow> aggregates = rows.ToList();

        if (aggregates.Count == 0)
        {
            return Empty(query.CourseId);
        }

        var summaryList = new List<QuizSummaryLookupDto>(aggregates.Count);
        foreach (Guid[] batch in aggregates.Select(a => a.QuizId).Chunk(SUMMARY_BATCH_SIZE))
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

        List<AggregateRow> kept = aggregates
            .Where(a => summaries.ContainsKey(a.QuizId))
            .ToList();

        if (kept.Count == 0)
        {
            return Empty(query.CourseId);
        }

        List<CourseQuizStatsRow> quizzes = kept
            .Select(a => new CourseQuizStatsRow(
                a.QuizId,
                summaries[a.QuizId].Title,
                a.AttemptsCount,
                a.UniqueUsers,
                a.AttemptsCount == 0 ? 0 : Math.Round(100.0 * a.PassedCount / a.AttemptsCount, 1),
                a.AttemptsCount == 0 ? 0 : Math.Round((double)a.ScoreSum / a.AttemptsCount, 1)))
            .OrderByDescending(q => q.AttemptsCount)
            .ThenBy(q => q.Title, StringComparer.Ordinal)
            .ToList();

        long totalAttempts = kept.Sum(a => a.AttemptsCount);
        long totalPassed = kept.Sum(a => a.PassedCount);
        long totalScore = kept.Sum(a => a.ScoreSum);

        return new CourseQuizStatsOverviewResponse(
            query.CourseId,
            quizzes.Count,
            totalAttempts,
            totalAttempts == 0 ? 0 : Math.Round(100.0 * totalPassed / totalAttempts, 1),
            totalAttempts == 0 ? 0 : Math.Round((double)totalScore / totalAttempts, 1),
            quizzes);
    }

    private static CourseQuizStatsOverviewResponse Empty(Guid courseId) =>
        new(courseId, 0, 0, 0, 0, []);

    private sealed class AggregateRow
    {
        public Guid QuizId { get; init; }

        public long AttemptsCount { get; init; }

        public long UniqueUsers { get; init; }

        public long PassedCount { get; init; }

        public long ScoreSum { get; init; }
    }
}