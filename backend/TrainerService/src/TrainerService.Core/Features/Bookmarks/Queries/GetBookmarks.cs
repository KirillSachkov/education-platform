using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts;
using TrainerService.Contracts.Bookmarks;
using TrainerService.Core.Database;
using TrainerService.Domain.Bookmarks;
using TrainerService.Domain.Questions;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Bookmarks.Queries;

public sealed record GetBookmarksQuery(Guid UserId, string? Cursor, int Limit) : IQuery;

public sealed class GetBookmarksEndpoint : IEndpoint
{
    public const int DEFAULT_LIMIT = 30;
    public const int MAX_LIMIT = 50;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/bookmarks",
                async Task<EndpointResult<CursorResponse<BookmarkDto>>> (
                    GetBookmarksHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken,
                    string? cursor = null,
                    int? limit = null) =>
                    await handler.Handle(
                        new GetBookmarksQuery(user.UserId, cursor, ClampLimit(limit)),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }

    private static int ClampLimit(int? limit) =>
        limit is { } value && value > 0 ? Math.Min(value, MAX_LIMIT) : DEFAULT_LIMIT;
}

/// <summary>
///     Закладки вызывающего (newest-first, keyset-пагинация по <c>CreatedAt DESC, Id DESC</c> —
///     единственный неограниченный trainer-список, #568), обогащённые контентом вопроса: текст
///     вопроса (стем), сложность и заголовок темы — чтобы UI показал реальный вопрос и дал войти
///     в тест по закладке. Обогащение ECS-стемами/темами применяется ТОЛЬКО к странице. Стем/
///     сложность тянутся из ECS answer-key каждого закладочного квиза (дедуп S2S-fetch по quizId,
///     как <c>GetMistakes</c>), заголовок темы — из локального репозитория тем. <b>Ключ грейдинга НЕ
///     раскрывается</b> (только стем — закладка это превью). Удалённый в ECS вопрос → стем null
///     (закладка всё равно возвращается, не падаем); недоступность ECS деградирует мягко (стемы
///     null), чтобы список закладок оставался доступным. Битый/невалидный cursor → первая страница.
///     Own-data.
/// </summary>
public sealed class GetBookmarksHandler : IQueryHandlerWithResult<CursorResponse<BookmarkDto>, GetBookmarksQuery>
{
    private readonly IBookmarkedQuestionsRepository _bookmarks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly ITopicsRepository _topics;

    public GetBookmarksHandler(
        IBookmarkedQuestionsRepository bookmarks,
        ITrainerQuestionsRepository questions,
        ITopicsRepository topics)
    {
        _bookmarks = bookmarks;
        _questions = questions;
        _topics = topics;
    }

    public async Task<Result<CursorResponse<BookmarkDto>, Error>> Handle(
        GetBookmarksQuery query,
        CancellationToken cancellationToken)
    {
        Cursor? cursor = Cursor.Decode(query.Cursor);

        // limit + 1 — лишняя строка детектит «есть ещё», в страницу не попадает.
        IReadOnlyList<BookmarkedQuestion> page = await _bookmarks.GetPageForUserAsync(
            query.UserId,
            cursor?.CreatedAt.UtcDateTime,
            cursor?.LastId,
            query.Limit + 1,
            cancellationToken);

        bool hasMore = page.Count > query.Limit;
        IReadOnlyList<BookmarkedQuestion> bookmarks = hasMore ? page.Take(query.Limit).ToList() : page;

        if (bookmarks.Count == 0)
            return new CursorResponse<BookmarkDto> { Items = [], NextCursor = null };

        // Стем/сложность вопроса — из собственного банка тренажёра (#623) по id вопросов страницы.
        // Удалённый вопрос → нет строки → стем останется null (закладка всё равно возвращается).
        var pageQuestionIds = bookmarks.Select(b => b.QuestionId).Distinct().ToList();
        var questionContent = (await _questions.GetManyByAsync(
                q => pageQuestionIds.Contains(q.Id), cancellationToken))
            .GroupBy(q => q.Id)
            .ToDictionary(g => g.Key, g => (Stem: g.First().Stem, Difficulty: g.First().Difficulty?.ToString()));

        // Заголовки тем — локально (закладки старого формата без TopicId резолвятся как null).
        var topicIds = bookmarks
            .Where(b => b.TopicId is { } id && id != Guid.Empty)
            .Select(b => b.TopicId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> topicTitleById = topicIds.Count == 0
            ? []
            : (await _topics.GetManyByAsync(t => topicIds.Contains(t.Id), cancellationToken))
                .ToDictionary(t => t.Id, t => t.Title);

        IReadOnlyList<BookmarkDto> items = bookmarks
            .Select(b =>
            {
                questionContent.TryGetValue(b.QuestionId, out (string Stem, string? Difficulty) content);
                string? topicTitle = b.TopicId is { } tid && topicTitleById.TryGetValue(tid, out string? title)
                    ? title
                    : null;

                return new BookmarkDto(
                    b.Id,
                    b.TopicId,
                    b.QuestionId,
                    b.CreatedAt,
                    content.Stem, // null если вопрос удалён из банка.
                    content.Difficulty,
                    topicTitle);
            })
            .ToList();

        BookmarkedQuestion last = bookmarks[^1];
        string? nextCursor = hasMore
            ? Cursor.Encode(DateTime.SpecifyKind(last.CreatedAt, DateTimeKind.Utc), last.Id)
            : null;

        return new CursorResponse<BookmarkDto> { Items = items, NextCursor = nextCursor };
    }
}
