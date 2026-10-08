using ContentAccess;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Questions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain.QuestionStudyStates;

namespace TrainerService.Core.Features.Questions.Queries;

public sealed record GetMistakesQuery(Guid UserId, bool IsAdmin, Guid? TopicId, string? Difficulty, int Limit) : IQuery;

public sealed class GetMistakesEndpoint : IEndpoint
{
    public const int DEFAULT_LIMIT = 50;
    public const int MAX_LIMIT = 200;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/mistakes",
                async Task<EndpointResult<IReadOnlyList<MistakeItemDto>>> (
                    GetMistakesHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken,
                    Guid? topicId = null,
                    string? difficulty = null,
                    int? limit = null) =>
                    await handler.Handle(
                        new GetMistakesQuery(user.UserId, user.IsAdmin, topicId, difficulty, Math.Clamp(limit ?? DEFAULT_LIMIT, 1, MAX_LIMIT)),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Кросс-тематический список «Мои ошибки» вызывающего (#568 Ф2): study-state'ы со статусом
///     WRONG/REVIEW (WRONG — живой сигнал), опц. сужено по теме (SQL) и сложности (in-memory, после
///     enrichment'а — difficulty живёт в ECS). Most-recent-wrong first, SQL-bounded. Обогащает
///     стемом из ECS. Только собственные данные (scoped по UserId). Недоступность ECS — фейл.
/// </summary>
public sealed class GetMistakesHandler : IQueryHandlerWithResult<IReadOnlyList<MistakeItemDto>, GetMistakesQuery>
{
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly QuestionContentResolver _contentResolver;
    private readonly IEntitlementChecker _entitlements;

    public GetMistakesHandler(
        IQuestionStudyStatesRepository studyStates,
        QuestionContentResolver contentResolver,
        IEntitlementChecker entitlements)
    {
        _studyStates = studyStates;
        _contentResolver = contentResolver;
        _entitlements = entitlements;
    }

    public async Task<Result<IReadOnlyList<MistakeItemDto>, Error>> Handle(
        GetMistakesQuery query,
        CancellationToken cancellationToken)
    {
        Guid? topicFilter = query.TopicId is { } id && id != Guid.Empty ? id : null;

        IReadOnlyList<QuestionStudyState> mistakes =
            await _studyStates.GetMistakesForUserAsync(query.UserId, topicFilter, query.Limit, cancellationToken);
        if (mistakes.Count == 0)
            return new List<MistakeItemDto>();

        var topicIds = mistakes.Select(s => s.TopicId).Distinct().ToList();
        Result<IReadOnlyDictionary<Guid, ResolvedQuestion>, Error> contentResult =
            await _contentResolver.ResolveAsync(topicIds, cancellationToken);
        if (contentResult.IsFailure)
            return contentResult.Error;

        IReadOnlyDictionary<Guid, ResolvedQuestion> content = contentResult.Value;
        string? difficultyFilter = string.IsNullOrWhiteSpace(query.Difficulty) ? null : query.Difficulty.Trim();

        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            query.UserId, query.IsAdmin, _entitlements, cancellationToken);

        // Per-question замок (#674): не-PRO + не-free-сэмпл → RedactLocked гасит стем (метаданные остаются).
        IReadOnlyList<MistakeItemDto> items = mistakes
            .Where(s => content.ContainsKey(s.QuestionId))
            .Select(s => (State: s, Resolved: content[s.QuestionId]))
            .Where(x => difficultyFilter == null
                || string.Equals(x.Resolved.Difficulty, difficultyFilter, StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                bool locked = !hasPro && !x.Resolved.IsFreeSample;
                return LockedContentRedactor.RedactLocked(new MistakeItemDto(
                    x.State.QuestionId,
                    x.State.TopicId,
                    x.Resolved.Stem,
                    x.Resolved.Difficulty,
                    x.State.Status.ToString(),
                    x.State.TimesWrong,
                    x.State.LastSeenAt.UtcDateTime,
                    x.State.NextDueAt?.UtcDateTime,
                    locked,
                    locked ? LockedContentRedactor.LOCK_REASON_PRO_REQUIRED : null));
            })
            .ToList();

        return Result.Success<IReadOnlyList<MistakeItemDto>, Error>(items);
    }
}
