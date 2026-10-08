using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Admin;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetQuestionQualityStatsQuery(int Days) : IQuery;

public sealed class GetQuestionQualityStatsEndpoint : IEndpoint
{
    public const int DEFAULT_DAYS = 30;
    public const int MIN_DAYS = 1;
    public const int MAX_DAYS = 365;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/admin/stats/question-quality",
                async Task<EndpointResult<AdminQuestionQualityStatsDto>> (
                    GetQuestionQualityStatsHandler handler,
                    CancellationToken cancellationToken,
                    int? days = null) =>
                    await handler.Handle(
                        new GetQuestionQualityStatsQuery(Math.Clamp(days ?? DEFAULT_DAYS, MIN_DAYS, MAX_DAYS)),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN)
            .RequireRateLimiting("admin-stats");
    }
}

/// <summary>
///     Admin question-quality дашборд тренажёра (#681 T4): по-вопросные метрики качества банка за окно
///     <c>days</c> (clamp 1..365, default 30). Read-only Dapper-агрегаты (raw SQL) поверх снапшотов
///     ответов <c>trainer.training_session_items</c>, джойнятся к собственным вопросам
///     (<c>trainer_questions → topic_banks → topics</c>). Зеркалит <see cref="GetAdminStatsHandler"/> по
///     стилю (несколько SQL-запросов, сборка в C#, без EF-загрузки сущностей).
///     <para>
///     <b>Скоуп окна.</b> Все метрики берут item'ы сессий с <c>started_at &gt;= now - days</c>. Так в
///     охват попадают и отвеченные, и пропущенные (<c>answered_at NULL</c>) item'ы — пропущенные нельзя
///     отфильтровать по <c>answered_at</c>, а они нужны для skip-rate.
///     </para>
///     <para>
///     <b>Метрики.</b> %-верных = CORRECT / отвеченные (по вердикту). Discrimination = прокси
///     point-biserial: (%-верных у сильной NTILE-группы − у слабой), сильные/слабые = NTILE(2)
///     пользователей по их общей точности в окне. Skip-rate = пропущенные / все item'ы вопроса в
///     ЗАВЕРШЁННЫХ сессиях. Time-per-question = средняя разница соседних <c>answered_at</c> внутри
///     сессии (нет served-at-колонки — приближение; отброшены неположительные и выбросы &gt; cap).
///     OPEN_TEXT — разбивка вердиктов + бэкеты балла (0–39/40–79/80–100, как у грейдера #678).
///     </para>
/// </summary>
public sealed class GetQuestionQualityStatsHandler
    : IQueryHandlerWithResult<AdminQuestionQualityStatsDto, GetQuestionQualityStatsQuery>
{
    /// <summary>
    ///     Диффы соседних ответов выше этой отсечки считаем «отошёл от экрана» и выбрасываем из среднего
    ///     времени — иначе единичная долгая пауза разносит метрику. 10 минут — щедро для одного вопроса.
    /// </summary>
    private const int OUTLIER_CAP_SECONDS = 600;

    private readonly ITransactionManager _transactions;

    public GetQuestionQualityStatsHandler(ITransactionManager transactions) => _transactions = transactions;

    public async Task<Result<AdminQuestionQualityStatsDto, Error>> Handle(
        GetQuestionQualityStatsQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow.AddDays(-query.Days);

        DbConnection connection = _transactions.GetDbConnection();

        object args = new { Cutoff = cutoffUtc, OutlierCap = OUTLIER_CAP_SECONDS };

        // --- Per-question aggregates (correctness, skip-rate, open-text verdict split + score buckets) ---
        // COUNT(*) FILTER lets one GROUP BY produce every counter. Score buckets mirror the grader bands
        // (#678): 0-39 INCORRECT / 40-79 PARTIAL / 80-100 CORRECT — bucketed by raw score, not verdict.
        const string perQuestionSql = """
            SELECT
                q.id                                                          AS QuestionId,
                q.stem                                                        AS Stem,
                q.type                                                        AS QuestionType,
                q.difficulty                                                  AS Difficulty,
                q.section                                                     AS Section,
                t.id                                                          AS TopicId,
                t.title                                                       AS TopicTitle,
                tb.id                                                         AS BankId,
                COUNT(*) FILTER (WHERE i.answered_at IS NOT NULL)             AS Attempts,
                COUNT(*) FILTER (WHERE i.verdict = 'CORRECT')                 AS CorrectCount,
                COUNT(*) FILTER (WHERE s.status = 'COMPLETED')                AS CompletedItems,
                COUNT(*) FILTER (WHERE s.status = 'COMPLETED'
                                   AND i.answered_at IS NULL)                 AS SkippedItems,
                COUNT(*) FILTER (WHERE q.type = 'OPEN_TEXT'
                                   AND i.verdict = 'CORRECT')                 AS OpenCorrect,
                COUNT(*) FILTER (WHERE q.type = 'OPEN_TEXT'
                                   AND i.verdict = 'PARTIAL')                 AS OpenPartial,
                COUNT(*) FILTER (WHERE q.type = 'OPEN_TEXT'
                                   AND i.verdict = 'INCORRECT')               AS OpenIncorrect,
                COUNT(*) FILTER (WHERE q.type = 'OPEN_TEXT'
                                   AND i.score_percent BETWEEN 0 AND 39)      AS ScoreLow,
                COUNT(*) FILTER (WHERE q.type = 'OPEN_TEXT'
                                   AND i.score_percent BETWEEN 40 AND 79)     AS ScoreMid,
                COUNT(*) FILTER (WHERE q.type = 'OPEN_TEXT'
                                   AND i.score_percent BETWEEN 80 AND 100)    AS ScoreHigh
            FROM trainer_questions q
            JOIN topic_banks tb ON tb.id = q.bank_id
            JOIN topics t       ON t.id = tb.topic_id
            JOIN training_session_items i ON i.question_id = q.id
            JOIN training_sessions s      ON s.id = i.session_id
            WHERE s.started_at >= @Cutoff
            GROUP BY q.id, q.stem, q.type, q.difficulty, q.section, t.id, t.title, tb.id;
            """;

        // --- Discrimination (point-biserial-style proxy) ---
        // First, each user's accuracy over all of their answered items in the window. Then users are
        // split via NTILE(2) over that accuracy — tile 2 is the strong (top) group, tile 1 the weak
        // (bottom) group. Per question, the average correctness within each tile is returned, and the
        // handler reports the strong-group rate minus the weak-group rate as discrimination (null when
        // a tile has no answers for that question, e.g. fewer than two distinguishable users).
        const string discriminationSql = """
            WITH scoped AS (
                SELECT i.question_id,
                       s.user_id,
                       CASE WHEN i.verdict = 'CORRECT' THEN 1.0 ELSE 0.0 END AS correct
                FROM training_session_items i
                JOIN training_sessions s ON s.id = i.session_id
                WHERE s.started_at >= @Cutoff AND i.answered_at IS NOT NULL
            ),
            user_acc AS (
                SELECT user_id, AVG(correct) AS accuracy
                FROM scoped
                GROUP BY user_id
            ),
            tiled AS (
                SELECT user_id, NTILE(2) OVER (ORDER BY accuracy) AS tile
                FROM user_acc
            )
            SELECT sc.question_id                                       AS QuestionId,
                   (AVG(sc.correct) FILTER (WHERE t.tile = 2))::double precision AS TopRate,
                   (AVG(sc.correct) FILTER (WHERE t.tile = 1))::double precision AS BottomRate
            FROM scoped sc
            JOIN tiled t ON t.user_id = sc.user_id
            GROUP BY sc.question_id;
            """;

        // --- Time-per-question (approximation; NO served-at/duration column) ---
        // Diff between consecutive answered_at within a session, attributed to the SECOND (current) answer.
        // The first answered item in a session has no predecessor → no sample. Guard: drop non-positive
        // (clock skew / equal timestamps) and outliers above the cap.
        const string timeSql = """
            WITH ordered AS (
                SELECT i.question_id,
                       i.answered_at,
                       LAG(i.answered_at) OVER (
                           PARTITION BY i.session_id
                           ORDER BY i.answered_at, i.sort_index) AS prev_at
                FROM training_session_items i
                JOIN training_sessions s ON s.id = i.session_id
                WHERE s.started_at >= @Cutoff AND i.answered_at IS NOT NULL
            ),
            diffs AS (
                SELECT question_id,
                       EXTRACT(EPOCH FROM (answered_at - prev_at)) AS secs
                FROM ordered
                WHERE prev_at IS NOT NULL
            )
            SELECT question_id          AS QuestionId,
                   AVG(secs)::double precision AS AvgSeconds,
                   COUNT(*)             AS SampleCount
            FROM diffs
            WHERE secs > 0 AND secs <= @OutlierCap
            GROUP BY question_id;
            """;

        IReadOnlyList<PerQuestionRow> perQuestionRows =
            (await connection.QueryAsync<PerQuestionRow>(
                new CommandDefinition(perQuestionSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<DiscriminationRow> discriminationRows =
            (await connection.QueryAsync<DiscriminationRow>(
                new CommandDefinition(discriminationSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<TimeRow> timeRows =
            (await connection.QueryAsync<TimeRow>(
                new CommandDefinition(timeSql, args, cancellationToken: cancellationToken))).ToList();

        Dictionary<Guid, DiscriminationRow> discriminationByQuestion =
            discriminationRows.ToDictionary(r => r.QuestionId);
        Dictionary<Guid, TimeRow> timeByQuestion = timeRows.ToDictionary(r => r.QuestionId);

        List<AdminQuestionQualityItemDto> questions = perQuestionRows
            .Select(r => BuildItem(
                r,
                discriminationByQuestion.GetValueOrDefault(r.QuestionId),
                timeByQuestion.GetValueOrDefault(r.QuestionId)))
            // Worst, well-sampled questions first — natural «переписать»-triage default; FE re-sorts.
            .OrderBy(q => q.CorrectRate ?? double.MaxValue)
            .ThenByDescending(q => q.Attempts)
            .ThenBy(q => q.QuestionId)
            .ToList();

        return new AdminQuestionQualityStatsDto(query.Days, OUTLIER_CAP_SECONDS, questions);
    }

    private static AdminQuestionQualityItemDto BuildItem(
        PerQuestionRow row,
        DiscriminationRow? discrimination,
        TimeRow? time)
    {
        double? correctRate = row.Attempts > 0 ? (double)row.CorrectCount / row.Attempts : null;

        double? topRate = discrimination?.TopRate;
        double? bottomRate = discrimination?.BottomRate;
        double? discriminationValue =
            topRate.HasValue && bottomRate.HasValue ? topRate.Value - bottomRate.Value : null;

        double? skipRate = row.CompletedItems > 0 ? (double)row.SkippedItems / row.CompletedItems : null;

        bool isOpenText = string.Equals(row.QuestionType, "OPEN_TEXT", StringComparison.Ordinal);
        AdminOpenTextQualityDto? openText = isOpenText
            ? new AdminOpenTextQualityDto(
                row.OpenCorrect,
                row.OpenPartial,
                row.OpenIncorrect,
                row.ScoreLow,
                row.ScoreMid,
                row.ScoreHigh)
            : null;

        return new AdminQuestionQualityItemDto(
            row.QuestionId,
            row.Stem,
            row.QuestionType,
            row.Difficulty,
            row.Section,
            row.TopicId,
            row.TopicTitle,
            row.BankId,
            row.Attempts,
            row.CorrectCount,
            correctRate,
            discriminationValue,
            topRate,
            bottomRate,
            row.CompletedItems,
            row.SkippedItems,
            skipRate,
            time?.AvgSeconds,
            time?.SampleCount ?? 0L,
            openText);
    }

    private sealed record PerQuestionRow
    {
        public Guid QuestionId { get; init; }
        public string Stem { get; init; } = null!;
        public string QuestionType { get; init; } = null!;
        public string? Difficulty { get; init; }
        public string? Section { get; init; }
        public Guid TopicId { get; init; }
        public string TopicTitle { get; init; } = null!;
        public Guid BankId { get; init; }
        public long Attempts { get; init; }
        public long CorrectCount { get; init; }
        public long CompletedItems { get; init; }
        public long SkippedItems { get; init; }
        public long OpenCorrect { get; init; }
        public long OpenPartial { get; init; }
        public long OpenIncorrect { get; init; }
        public long ScoreLow { get; init; }
        public long ScoreMid { get; init; }
        public long ScoreHigh { get; init; }
    }

    private sealed record DiscriminationRow(Guid QuestionId, double? TopRate, double? BottomRate);

    private sealed record TimeRow(Guid QuestionId, double? AvgSeconds, long SampleCount);
}
