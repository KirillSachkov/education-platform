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

public sealed record GetFeedbackRatingStatsQuery(int Days) : IQuery;

public sealed class GetFeedbackRatingStatsEndpoint : IEndpoint
{
    public const int DEFAULT_DAYS = 30;
    public const int MIN_DAYS = 1;
    public const int MAX_DAYS = 365;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/admin/stats/feedback-ratings",
                async Task<EndpointResult<AdminFeedbackRatingStatsDto>> (
                    GetFeedbackRatingStatsHandler handler,
                    CancellationToken cancellationToken,
                    int? days = null) =>
                    await handler.Handle(
                        new GetFeedbackRatingStatsQuery(Math.Clamp(days ?? DEFAULT_DAYS, MIN_DAYS, MAX_DAYS)),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN)
            .RequireRateLimiting("admin-stats");
    }
}

/// <summary>
///     Admin-агрегат оценок AI-разбора тренажёра (#691 t7): по-вопросная разбивка 👍/👎 за окно
///     <c>days</c> (clamp 1..365, default 30). Read-only Dapper-агрегат (raw SQL) поверх
///     <c>trainer.ai_feedback_ratings</c>, джойнится к собственным вопросам тренажёра
///     (<c>trainer_questions → topic_banks → topics</c>). Скоуп окна — по <c>created_at &gt;= now - days</c>.
///     Упорядочено «худшие сверху» (наибольший down-rate, затем по числу 👎) — так владелец видит
///     вопросы, где AI-разбор студентам не нравится, и может править промпт/эталон. Зеркалит стиль
///     <see cref="GetQuestionQualityStatsHandler"/> (raw SQL, сборка в C#, без EF-загрузки).
/// </summary>
public sealed class GetFeedbackRatingStatsHandler
    : IQueryHandlerWithResult<AdminFeedbackRatingStatsDto, GetFeedbackRatingStatsQuery>
{
    private readonly ITransactionManager _transactions;

    public GetFeedbackRatingStatsHandler(ITransactionManager transactions) => _transactions = transactions;

    public async Task<Result<AdminFeedbackRatingStatsDto, Error>> Handle(
        GetFeedbackRatingStatsQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow.AddDays(-query.Days);

        DbConnection connection = _transactions.GetDbConnection();

        // One GROUP BY produces per-question up/down counts. Inner-join to trainer_questions drops
        // ratings whose question was deleted (the owner can't act on a gone question anyway).
        const string sql = """
            SELECT
                q.id                                          AS QuestionId,
                q.stem                                        AS Stem,
                q.type                                        AS QuestionType,
                q.difficulty                                  AS Difficulty,
                t.id                                          AS TopicId,
                t.title                                       AS TopicTitle,
                tb.id                                         AS BankId,
                COUNT(*) FILTER (WHERE r.rating = 'UP')       AS Up,
                COUNT(*) FILTER (WHERE r.rating = 'DOWN')     AS Down,
                COUNT(*)                                      AS Total
            FROM ai_feedback_ratings r
            JOIN trainer_questions q ON q.id = r.question_id
            JOIN topic_banks tb      ON tb.id = q.bank_id
            JOIN topics t            ON t.id = tb.topic_id
            WHERE r.created_at >= @Cutoff
            GROUP BY q.id, q.stem, q.type, q.difficulty, t.id, t.title, tb.id;
            """;

        IReadOnlyList<RatingRow> rows =
            (await connection.QueryAsync<RatingRow>(
                new CommandDefinition(sql, new { Cutoff = cutoffUtc }, cancellationToken: cancellationToken)))
            .ToList();

        List<AdminFeedbackRatingItemDto> questions = rows
            .Select(r => new AdminFeedbackRatingItemDto(
                r.QuestionId,
                r.Stem,
                r.QuestionType,
                r.Difficulty,
                r.TopicId,
                r.TopicTitle,
                r.BankId,
                r.Up,
                r.Down,
                r.Total,
                r.Total > 0 ? (double)r.Down / r.Total : 0))
            // Worst (most-disliked) first — the natural «починить промпт»-triage default; FE can re-sort.
            .OrderByDescending(q => q.DownRate)
            .ThenByDescending(q => q.Down)
            .ThenBy(q => q.QuestionId)
            .ToList();

        return new AdminFeedbackRatingStatsDto(query.Days, questions);
    }

    private sealed record RatingRow
    {
        public Guid QuestionId { get; init; }
        public string Stem { get; init; } = null!;
        public string QuestionType { get; init; } = null!;
        public string? Difficulty { get; init; }
        public Guid TopicId { get; init; }
        public string TopicTitle { get; init; } = null!;
        public Guid BankId { get; init; }
        public long Up { get; init; }
        public long Down { get; init; }
        public long Total { get; init; }
    }
}
