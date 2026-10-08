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

public sealed record GetTopicBankStatsQuery(int Days) : IQuery;

public sealed class GetTopicBankStatsEndpoint : IEndpoint
{
    public const int DEFAULT_DAYS = 30;
    public const int MIN_DAYS = 1;
    public const int MAX_DAYS = 365;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/admin/stats/topics",
                async Task<EndpointResult<AdminTopicBankStatsDto>> (
                    GetTopicBankStatsHandler handler,
                    CancellationToken cancellationToken,
                    int? days = null) =>
                    await handler.Handle(
                        new GetTopicBankStatsQuery(Math.Clamp(days ?? DEFAULT_DAYS, MIN_DAYS, MAX_DAYS)),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN)
            .RequireRateLimiting("admin-stats");
    }
}

/// <summary>
///     Admin/автор контент-аналитика тренажёра (#681 T5): per-topic / per-bank срезы + калибровка сложности.
///     Read-only Dapper-агрегаты (raw SQL GROUP BY), без EF-загрузки сущностей — зеркалит
///     <c>GetAdminStats</c>. Окно <c>days</c> (clamp 1..365, default 30) применяется к АКТИВНОСТИ
///     (оценённые ответы за <c>answered_at &gt;= now-days</c>) и калибровке; агрегатный mastery темы —
///     текущий снимок (derived — взвешенное среднее последних баллов по уникальным вопросам; не оконный,
///     у <c>topic_masteries</c> нет per-answer времени).
///     <list type="bullet">
///         <item><b>Per-topic</b>: средний mastery (снимок), средний %-верных и объём ответов/сессий за окно;
///         темы сортируются «сложные сверху» (активные с наименьшим %-верных вперёд).</item>
///         <item><b>Per-bank</b>: покрытие (сколько вопросов банка реально отвечают vs всего) + разбивка
///         состава по типу и сложности.</item>
///         <item><b>Калибровка</b>: заявленная сложность вопроса vs фактический %-верных, по уровням
///         (JUNIOR/MIDDLE/SENIOR zero-filled + UNSPECIFIED), + вопросы-выбросы (дельта к среднему уровня).</item>
///     </list>
/// </summary>
public sealed class GetTopicBankStatsHandler
    : IQueryHandlerWithResult<AdminTopicBankStatsDto, GetTopicBankStatsQuery>
{
    /// <summary>Уровни сложности всегда возвращаются в этом порядке, нулями при отсутствии данных.</summary>
    private static readonly string[] DifficultyOrder = ["JUNIOR", "MIDDLE", "SENIOR"];

    private const string UNSPECIFIED = "UNSPECIFIED";

    /// <summary>Вопрос попадает в список выбросов только при ≥ этого числа оценённых ответов (отсев шума малой выборки).</summary>
    private const int MIN_ANSWERS_FOR_CALIBRATION = 2;

    /// <summary>Сколько вопросов-выбросов вернуть (сильнейшее |отклонение| первыми).</summary>
    private const int MISCALIBRATED_CAP = 20;

    private const string TopicFallbackTitle = "Тема";

    private readonly ITransactionManager _transactions;

    public GetTopicBankStatsHandler(ITransactionManager transactions) => _transactions = transactions;

    public async Task<Result<AdminTopicBankStatsDto, Error>> Handle(
        GetTopicBankStatsQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow.AddDays(-query.Days);

        DbConnection connection = _transactions.GetDbConnection();

        object args = new { Cutoff = cutoffUtc, MinAnswers = MIN_ANSWERS_FOR_CALIBRATION };

        // --- Per-topic: mastery snapshot (all-time) + windowed answer activity ---

        // Mastery is a derived snapshot (difficulty-weighted average of latest per-question scores)
        // with no per-answer timestamp → not windowed by design.
        const string topicMasterySql = """
            SELECT
                topic_id                          AS TopicId,
                ROUND(AVG(mastery_percent))::int  AS AvgMasteryPercent,
                COUNT(*)::int                     AS MasteryUsers
            FROM topic_masteries
            GROUP BY topic_id;
            """;

        // Answered items carry their own topic_id snapshot (multi-topic MOCK-safe) and answered_at.
        const string topicActivitySql = """
            SELECT
                topic_id                                  AS TopicId,
                ROUND(AVG(score_percent), 2)::float8      AS AvgCorrectPercent,
                COUNT(*)::bigint                          AS AnswersCount,
                COUNT(DISTINCT session_id)::int           AS SessionsCount
            FROM training_session_items
            WHERE score_percent IS NOT NULL AND answered_at >= @Cutoff
            GROUP BY topic_id;
            """;

        // --- Per-bank: composition (all banks, incl. empty) + windowed coverage ---

        const string bankSql = """
            SELECT
                b.id        AS BankId,
                b.topic_id  AS TopicId,
                b.tier      AS Tier,
                b.difficulty AS Difficulty,
                b.purpose   AS Purpose,
                COUNT(q.id)::int AS TotalQuestions
            FROM topic_banks b
            LEFT JOIN trainer_questions q ON q.bank_id = b.id
            GROUP BY b.id, b.topic_id, b.tier, b.difficulty, b.purpose;
            """;

        const string bankByTypeSql = """
            SELECT bank_id AS BankId, type AS Bucket, COUNT(*)::int AS Count
            FROM trainer_questions
            GROUP BY bank_id, type
            ORDER BY bank_id, type;
            """;

        const string bankByDifficultySql = """
            SELECT bank_id AS BankId, COALESCE(difficulty, 'UNSPECIFIED') AS Bucket, COUNT(*)::int AS Count
            FROM trainer_questions
            GROUP BY bank_id, difficulty
            ORDER BY bank_id, COALESCE(difficulty, 'UNSPECIFIED');
            """;

        // Coverage: distinct questions of a bank that got at least one graded answer in the window.
        const string bankAnsweredSql = """
            SELECT
                q.bank_id                       AS BankId,
                COUNT(DISTINCT i.question_id)::int   AS AnsweredQuestions,
                COUNT(*)::bigint                     AS AnswersCount
            FROM training_session_items i
            JOIN trainer_questions q ON q.id = i.question_id
            WHERE i.score_percent IS NOT NULL AND i.answered_at >= @Cutoff
            GROUP BY q.bank_id;
            """;

        // --- Calibration: declared difficulty (of the CURRENT question) vs actual %-correct ---

        const string calibrationLevelSql = """
            SELECT
                q.difficulty                          AS Difficulty,
                ROUND(AVG(i.score_percent), 2)::float8 AS ActualCorrectPercent,
                COUNT(*)::bigint                       AS AnswersCount,
                COUNT(DISTINCT i.question_id)::int     AS QuestionsAnswered
            FROM training_session_items i
            JOIN trainer_questions q ON q.id = i.question_id
            WHERE i.score_percent IS NOT NULL AND i.answered_at >= @Cutoff
            GROUP BY q.difficulty;
            """;

        const string calibrationQuestionSql = """
            SELECT
                i.question_id                         AS QuestionId,
                q.bank_id                             AS BankId,
                b.topic_id                            AS TopicId,
                q.difficulty                          AS Difficulty,
                q.stem                                AS Stem,
                ROUND(AVG(i.score_percent), 2)::float8 AS ActualCorrectPercent,
                COUNT(*)::bigint                       AS AnswersCount
            FROM training_session_items i
            JOIN trainer_questions q ON q.id = i.question_id
            JOIN topic_banks b ON b.id = q.bank_id
            WHERE i.score_percent IS NOT NULL AND i.answered_at >= @Cutoff AND q.difficulty IS NOT NULL
            GROUP BY i.question_id, q.bank_id, b.topic_id, q.difficulty, q.stem
            HAVING COUNT(*) >= @MinAnswers;
            """;

        IReadOnlyList<TopicMasteryRow> masteryRows =
            (await connection.QueryAsync<TopicMasteryRow>(
                new CommandDefinition(topicMasterySql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<TopicActivityRow> activityRows =
            (await connection.QueryAsync<TopicActivityRow>(
                new CommandDefinition(topicActivitySql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<BankRow> bankRows =
            (await connection.QueryAsync<BankRow>(
                new CommandDefinition(bankSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<BankBreakdownRow> bankTypeRows =
            (await connection.QueryAsync<BankBreakdownRow>(
                new CommandDefinition(bankByTypeSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<BankBreakdownRow> bankDiffRows =
            (await connection.QueryAsync<BankBreakdownRow>(
                new CommandDefinition(bankByDifficultySql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<BankAnsweredRow> bankAnsweredRows =
            (await connection.QueryAsync<BankAnsweredRow>(
                new CommandDefinition(bankAnsweredSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<CalibrationLevelRow> levelRows =
            (await connection.QueryAsync<CalibrationLevelRow>(
                new CommandDefinition(calibrationLevelSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<CalibrationQuestionRow> questionRows =
            (await connection.QueryAsync<CalibrationQuestionRow>(
                new CommandDefinition(calibrationQuestionSql, args, cancellationToken: cancellationToken))).ToList();

        Dictionary<Guid, string> titleByTopic = await ResolveTopicTitlesAsync(
            connection,
            masteryRows.Select(r => r.TopicId)
                .Concat(activityRows.Select(r => r.TopicId))
                .Concat(bankRows.Select(r => r.TopicId))
                .Concat(questionRows.Select(r => r.TopicId)),
            cancellationToken);

        string Title(Guid topicId) => titleByTopic.GetValueOrDefault(topicId, TopicFallbackTitle);

        return new AdminTopicBankStatsDto(
            query.Days,
            BuildTopics(masteryRows, activityRows, Title),
            BuildBanks(bankRows, bankTypeRows, bankDiffRows, bankAnsweredRows, Title),
            BuildCalibration(levelRows, questionRows, Title));
    }

    private static async Task<Dictionary<Guid, string>> ResolveTopicTitlesAsync(
        DbConnection connection,
        IEnumerable<Guid> topicIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = topicIds.Distinct().ToArray();
        if (ids.Length == 0)
            return [];

        const string sql = "SELECT id AS Id, title AS Title FROM topics WHERE id = ANY(@Ids);";
        IEnumerable<TopicTitleRow> rows = await connection.QueryAsync<TopicTitleRow>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));

        return rows.ToDictionary(r => r.Id, r => r.Title);
    }

    /// <summary>Merge mastery snapshot + windowed activity per topic; «сложные» (низкий %-верных) сверху.</summary>
    private static IReadOnlyList<AdminTopicStatDto> BuildTopics(
        IReadOnlyList<TopicMasteryRow> masteryRows,
        IReadOnlyList<TopicActivityRow> activityRows,
        Func<Guid, string> title)
    {
        Dictionary<Guid, TopicMasteryRow> masteryByTopic = masteryRows.ToDictionary(r => r.TopicId);
        Dictionary<Guid, TopicActivityRow> activityByTopic = activityRows.ToDictionary(r => r.TopicId);

        IEnumerable<Guid> topicIds = masteryByTopic.Keys.Union(activityByTopic.Keys);

        return topicIds
            .Select(id =>
            {
                masteryByTopic.TryGetValue(id, out TopicMasteryRow? m);
                activityByTopic.TryGetValue(id, out TopicActivityRow? a);
                return new AdminTopicStatDto(
                    id,
                    title(id),
                    m?.AvgMasteryPercent ?? 0,
                    m?.MasteryUsers ?? 0L,
                    a?.AvgCorrectPercent ?? 0d,
                    a?.AnswersCount ?? 0L,
                    a?.SessionsCount ?? 0L);
            })
            // Active topics first (have answers), hardest (lowest %-correct) at the top — surfaces «что трудно».
            .OrderByDescending(t => t.AnswersCount > 0)
            .ThenBy(t => t.AvgCorrectPercent)
            .ThenBy(t => t.TopicTitle, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<AdminBankStatDto> BuildBanks(
        IReadOnlyList<BankRow> bankRows,
        IReadOnlyList<BankBreakdownRow> bankTypeRows,
        IReadOnlyList<BankBreakdownRow> bankDiffRows,
        IReadOnlyList<BankAnsweredRow> bankAnsweredRows,
        Func<Guid, string> title)
    {
        Dictionary<Guid, List<AdminQuestionTypeCountDto>> typeByBank = bankTypeRows
            .GroupBy(r => r.BankId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new AdminQuestionTypeCountDto(r.Bucket, r.Count)).ToList());

        Dictionary<Guid, List<AdminQuestionDifficultyCountDto>> diffByBank = bankDiffRows
            .GroupBy(r => r.BankId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new AdminQuestionDifficultyCountDto(r.Bucket, r.Count)).ToList());

        Dictionary<Guid, BankAnsweredRow> answeredByBank = bankAnsweredRows.ToDictionary(r => r.BankId);

        return bankRows
            .Select(b =>
            {
                answeredByBank.TryGetValue(b.BankId, out BankAnsweredRow? ans);
                long answeredQuestions = ans?.AnsweredQuestions ?? 0L;
                long answersCount = ans?.AnswersCount ?? 0L;
                double coverage = b.TotalQuestions == 0
                    ? 0d
                    : Math.Round((double)answeredQuestions / b.TotalQuestions * 100d, 2);

                return new AdminBankStatDto(
                    b.BankId,
                    b.TopicId,
                    title(b.TopicId),
                    b.Tier,
                    b.Difficulty,
                    b.Purpose,
                    b.TotalQuestions,
                    answeredQuestions,
                    coverage,
                    answersCount,
                    typeByBank.TryGetValue(b.BankId, out List<AdminQuestionTypeCountDto>? types)
                        ? types
                        : [],
                    diffByBank.TryGetValue(b.BankId, out List<AdminQuestionDifficultyCountDto>? diffs)
                        ? diffs
                        : []);
            })
            // Lowest coverage first — surfaces под-используемые / пустые банки; deterministic tiebreak.
            .OrderBy(b => b.CoveragePercent)
            .ThenBy(b => b.TopicTitle, StringComparer.Ordinal)
            .ThenBy(b => b.BankId)
            .ToList();
    }

    private static AdminDifficultyCalibrationDto BuildCalibration(
        IReadOnlyList<CalibrationLevelRow> levelRows,
        IReadOnlyList<CalibrationQuestionRow> questionRows,
        Func<Guid, string> title)
    {
        Dictionary<string, CalibrationLevelRow> levelByDifficulty = levelRows
            .Where(r => r.Difficulty is not null)
            .ToDictionary(r => r.Difficulty!, StringComparer.Ordinal);

        List<AdminCalibrationLevelDto> levels = [];

        // JUNIOR/MIDDLE/SENIOR always present (zero-filled), like the mode-bucket convention.
        foreach (string difficulty in DifficultyOrder)
        {
            levelByDifficulty.TryGetValue(difficulty, out CalibrationLevelRow? row);
            levels.Add(new AdminCalibrationLevelDto(
                difficulty,
                row?.ActualCorrectPercent ?? 0d,
                row?.AnswersCount ?? 0L,
                row?.QuestionsAnswered ?? 0L));
        }

        // UNSPECIFIED appended only when questions without a declared difficulty were actually answered.
        CalibrationLevelRow? unspecified = levelRows.FirstOrDefault(r => r.Difficulty is null);
        if (unspecified is not null)
        {
            levels.Add(new AdminCalibrationLevelDto(
                UNSPECIFIED,
                unspecified.ActualCorrectPercent,
                unspecified.AnswersCount,
                unspecified.QuestionsAnswered));
        }

        List<AdminMiscalibratedQuestionDto> miscalibrated = questionRows
            .Select(q =>
            {
                // Baseline = the actual cohort average of the SAME declared level (data-driven, not a magic
                // constant). The question itself is part of that cohort, so the level row always exists.
                double cohortAverage = levelByDifficulty.TryGetValue(q.Difficulty, out CalibrationLevelRow? level)
                    ? level.ActualCorrectPercent
                    : q.ActualCorrectPercent;

                return new AdminMiscalibratedQuestionDto(
                    q.QuestionId,
                    q.BankId,
                    q.TopicId,
                    title(q.TopicId),
                    q.Difficulty,
                    q.Stem,
                    q.ActualCorrectPercent,
                    Math.Round(q.ActualCorrectPercent - cohortAverage, 2),
                    q.AnswersCount);
            })
            .OrderByDescending(q => Math.Abs(q.DeltaVsLevel))
            .ThenBy(q => q.QuestionId)
            .Take(MISCALIBRATED_CAP)
            .ToList();

        return new AdminDifficultyCalibrationDto(levels, miscalibrated);
    }

    // `::int`-cast counts map to int, `::bigint`-cast totals (AnswersCount) map to long.
    private sealed record TopicMasteryRow(Guid TopicId, int AvgMasteryPercent, int MasteryUsers);

    private sealed record TopicActivityRow(
        Guid TopicId,
        double AvgCorrectPercent,
        long AnswersCount,
        int SessionsCount);

    private sealed record BankRow(
        Guid BankId,
        Guid TopicId,
        string Tier,
        string? Difficulty,
        string Purpose,
        int TotalQuestions);

    // Shared shape for the by-type and by-difficulty breakdowns (the bucket label + a count).
    private sealed record BankBreakdownRow(Guid BankId, string Bucket, int Count);

    private sealed record BankAnsweredRow(Guid BankId, int AnsweredQuestions, long AnswersCount);

    private sealed record CalibrationLevelRow(
        string? Difficulty,
        double ActualCorrectPercent,
        long AnswersCount,
        int QuestionsAnswered);

    private sealed record CalibrationQuestionRow(
        Guid QuestionId,
        Guid BankId,
        Guid TopicId,
        string Difficulty,
        string Stem,
        double ActualCorrectPercent,
        long AnswersCount);

    private sealed record TopicTitleRow(Guid Id, string Title);
}
