using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Domain;

namespace ProgressService.Core.Features.QuizAttempts.Queries;

public sealed record GetMyQuizAttemptsSummaryQuery : IQuery;

public sealed class GetMyQuizAttemptsSummaryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/quizzes/attempts/my-summary",
                async Task<EndpointResult<MyQuizAttemptsSummaryResponse>> (
                    GetMyQuizAttemptsSummaryHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMyQuizAttemptsSummaryQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Сводка всех тестов, которые проходил текущий пользователь (страница «Мои тесты», #556).
///     Один round-trip в Postgres агрегирует <c>quiz_attempts</c> юзера по <c>quiz_id</c>
///     (число попыток, лучший балл, балл/результат последней попытки, прошёл ли хоть раз),
///     затем enrichment через ECS <see cref="IEducationContentServiceClient.GetQuizSummariesAsync"/>
///     (title + покрывающий курс). LEVEL_TEST-квизы отфильтровываются (у воронки своя
///     страница), как и квизы без summary (hard-deleted). Own-data: Tier-3 entitlement
///     не нужен — отдаются только собственные баллы пользователя, ключ ответов не утекает.
///     Сортировка результата — по последней активности (новые сверху).
/// </summary>
public sealed class GetMyQuizAttemptsSummaryHandler
    : IQueryHandlerWithResult<MyQuizAttemptsSummaryResponse, GetMyQuizAttemptsSummaryQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly UserScopedData _user;

    public GetMyQuizAttemptsSummaryHandler(
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
        _user = user;
    }

    public async Task<Result<MyQuizAttemptsSummaryResponse, Error>> Handle(
        GetMyQuizAttemptsSummaryQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // Агрегат по quiz_id: число попыток, лучший балл, балл/результат/время последней
        // попытки (по submitted_at desc, tie → id desc), passed = прошёл хоть раз.
        const string sql = """
                           SELECT
                               qa.quiz_id AS QuizId,
                               CAST(COUNT(*) AS int) AS AttemptsCount,
                               CAST(MAX(qa.score_percent) AS int) AS BestScorePercent,
                               bool_or(qa.passed) AS Passed,
                               (ARRAY_AGG(qa.score_percent ORDER BY qa.submitted_at DESC, qa.id DESC))[1] AS LastScorePercent,
                               MAX(qa.submitted_at) AS LastSubmittedAt
                           FROM quiz_attempts qa
                           WHERE qa.user_id = @UserId
                           GROUP BY qa.quiz_id;
                           """;

        IEnumerable<AttemptAggregateRow> rows = await connection.QueryAsync<AttemptAggregateRow>(
            new CommandDefinition(sql, new { UserId = _user.UserId }, cancellationToken: cancellationToken));

        List<AttemptAggregateRow> aggregates = rows.ToList();
        if (aggregates.Count == 0)
        {
            return new MyQuizAttemptsSummaryResponse([], TotalQuizzesTaken: 0, PassedCount: 0, AvgBestScorePercent: 0);
        }

        Guid[] quizIds = aggregates.Select(a => a.QuizId).ToArray();

        // ECS summaries-эндпоинт капается на 200 id'ов — чанкуем, иначе юзер, прошедший
        // >200 разных тестов, получает 503 вместо своей статистики (#556 review).
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

        List<MyQuizAttemptsSummaryItem> items = aggregates
            .Where(a => summaries.TryGetValue(a.QuizId, out QuizSummaryLookupDto? s)
                && !string.Equals(s.Purpose, "LEVEL_TEST", StringComparison.Ordinal))
            .Select(a =>
            {
                QuizSummaryLookupDto summary = summaries[a.QuizId];
                return new MyQuizAttemptsSummaryItem(
                    a.QuizId,
                    summary.Title,
                    summary.CourseId,
                    a.AttemptsCount,
                    a.BestScorePercent,
                    a.LastScorePercent,
                    a.LastSubmittedAt,
                    a.Passed);
            })
            .OrderByDescending(i => i.LastSubmittedAt)
            .ThenByDescending(i => i.QuizId)
            .ToList();

        int passedCount = items.Count(i => i.Passed);
        int avgBest = items.Count > 0
            ? (int)Math.Round(items.Average(i => i.BestScorePercent), MidpointRounding.AwayFromZero)
            : 0;

        return new MyQuizAttemptsSummaryResponse(items, items.Count, passedCount, avgBest);
    }

    private sealed class AttemptAggregateRow
    {
        public Guid QuizId { get; init; }

        public int AttemptsCount { get; init; }

        public int BestScorePercent { get; init; }

        public int LastScorePercent { get; init; }

        public DateTime LastSubmittedAt { get; init; }

        public bool Passed { get; init; }
    }
}
