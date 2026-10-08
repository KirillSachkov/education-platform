using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Quizzes.Queries;

public sealed record GetMyQuizzesQuery : IQuery;

/// <summary>
///     Карточка квиза в авторской библиотеке (ST-12 #492). DTO живёт в slice'е
///     (не в Contracts) — потребитель один, фронт ST-15. <c>Purpose</c> отдаётся как есть:
///     библиотека на фронте сама отфильтровывает LEVEL_TEST (у него своя страница).
///     Counts — usage-метрики для карточки: сколько материалов ссылаются на квиз
///     (<c>materials.quiz_id</c>) и в скольких курсах он размещён (<c>course_quizzes</c>).
/// </summary>
public sealed record MyQuizSummaryDto(
    Guid Id,
    string Title,
    string Status,
    string AccessType,
    string Purpose,
    int QuestionsCount,
    int PassingScorePercent,
    int UsedByMaterialsCount,
    int CourseCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed class GetMyQuizzesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("quizzes/mine", async Task<EndpointResult<IReadOnlyList<MyQuizSummaryDto>>> (
                    [FromServices] GetMyQuizzesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMyQuizzesQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Общая авторская библиотека квизов: ВСЕ квизы caller'а любого статуса и любого
///     purpose, новые сверху. Область видимости зеркалит Tier-2 ownership-семантику
///     <c>OwnershipExtensions.CheckOwnership</c> (как <see cref="GetMyLevelTestsHandler"/>):
///     автор видит только свои, admin / content-moderator — все. Ответы вопросов наружу
///     не идут — только агрегаты (questionsCount + counts), поэтому полная авторская
///     проекция не нужна.
/// </summary>
public sealed class GetMyQuizzesHandler
    : IQueryHandlerWithResult<IReadOnlyList<MyQuizSummaryDto>, GetMyQuizzesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _userScopedData;

    public GetMyQuizzesHandler(
        ITransactionManager transactionManager,
        UserScopedData userScopedData)
    {
        _transactionManager = transactionManager;
        _userScopedData = userScopedData;
    }

    public async Task<Result<IReadOnlyList<MyQuizSummaryDto>, Error>> Handle(
        GetMyQuizzesQuery query,
        CancellationToken cancellationToken)
    {
        bool seesAll = _userScopedData.IsAdmin
            || _userScopedData.HasPermission(PlatformPermissions.Content.MODERATE);
        Guid userId = _userScopedData.UserId;

        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                q.id,
                q.title,
                q.status,
                q.access_type,
                q.purpose,
                jsonb_array_length(q.questions) AS questions_count,
                q.passing_score_percent,
                (SELECT COUNT(*)::int FROM materials m WHERE m.quiz_id = q.id) AS used_by_materials_count,
                (SELECT COUNT(*)::int FROM course_quizzes cq WHERE cq.quiz_id = q.id) AS course_count,
                q.created_at,
                q.updated_at
            FROM quizzes q
            WHERE @SeesAll OR q.author_id = @UserId
            ORDER BY q.created_at DESC, q.id DESC
            """;

        IEnumerable<MyQuizRow> rows = await connection.QueryAsync<MyQuizRow>(
            new CommandDefinition(
                sql,
                new { SeesAll = seesAll, UserId = userId },
                cancellationToken: cancellationToken));

        return rows
            .Select(r => new MyQuizSummaryDto(
                r.Id, r.Title, r.Status, r.AccessType, r.Purpose,
                r.QuestionsCount, r.PassingScorePercent,
                r.UsedByMaterialsCount, r.CourseCount,
                r.CreatedAt, r.UpdatedAt))
            .ToList();
    }

    private sealed class MyQuizRow
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = null!;
        public string Status { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public string Purpose { get; init; } = null!;
        public int QuestionsCount { get; init; }
        public int PassingScorePercent { get; init; }
        public int UsedByMaterialsCount { get; init; }
        public int CourseCount { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
