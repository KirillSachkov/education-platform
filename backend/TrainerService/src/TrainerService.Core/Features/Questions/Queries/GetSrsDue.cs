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

public sealed record GetSrsDueQuery(Guid UserId, bool IsAdmin, int Limit) : IQuery;

public sealed class GetSrsDueEndpoint : IEndpoint
{
    public const int DEFAULT_LIMIT = 50;
    public const int MAX_LIMIT = 200;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/srs/due",
                async Task<EndpointResult<IReadOnlyList<SrsDueItemDto>>> (
                    GetSrsDueHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken,
                    int? limit = null) =>
                    await handler.Handle(
                        new GetSrsDueQuery(user.UserId, user.IsAdmin, Math.Clamp(limit ?? DEFAULT_LIMIT, 1, MAX_LIMIT)),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Кросс-тематическая SRS-очередь «на повтор сегодня» вызывающего (#568 Ф2): study-state'ы с
///     <c>next_due_at &lt;= now</c>, most-due first (SQL-bounded). Обогащает стемом из ECS. Только
///     собственные данные (scoped по UserId). Недоступность ECS — фейл (fail-closed); вопросы
///     удалённых квизов выпадают из enrichment'а и из выдачи.
/// </summary>
public sealed class GetSrsDueHandler : IQueryHandlerWithResult<IReadOnlyList<SrsDueItemDto>, GetSrsDueQuery>
{
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly QuestionContentResolver _contentResolver;
    private readonly IEntitlementChecker _entitlements;

    public GetSrsDueHandler(
        IQuestionStudyStatesRepository studyStates,
        QuestionContentResolver contentResolver,
        IEntitlementChecker entitlements)
    {
        _studyStates = studyStates;
        _contentResolver = contentResolver;
        _entitlements = entitlements;
    }

    public async Task<Result<IReadOnlyList<SrsDueItemDto>, Error>> Handle(
        GetSrsDueQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<QuestionStudyState> due = await _studyStates.GetManyForUserAsync(
            query.UserId, topicId: null, status: null, dueOnly: true, query.Limit, cancellationToken);
        if (due.Count == 0)
            return new List<SrsDueItemDto>();

        var topicIds = due.Select(s => s.TopicId).Distinct().ToList();
        Result<IReadOnlyDictionary<Guid, ResolvedQuestion>, Error> contentResult =
            await _contentResolver.ResolveAsync(topicIds, cancellationToken);
        if (contentResult.IsFailure)
            return contentResult.Error;

        IReadOnlyDictionary<Guid, ResolvedQuestion> content = contentResult.Value;

        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            query.UserId, query.IsAdmin, _entitlements, cancellationToken);

        // Сохраняем most-due порядок репозитория; вопросы без резолва (удалённый вопрос) выпадают.
        // Per-question замок (#674): не-PRO + не-free-сэмпл → RedactLocked гасит стем (метаданные остаются).
        IReadOnlyList<SrsDueItemDto> items = due
            .Where(s => content.ContainsKey(s.QuestionId))
            .Select(s =>
            {
                ResolvedQuestion resolved = content[s.QuestionId];
                bool locked = !hasPro && !resolved.IsFreeSample;
                return LockedContentRedactor.RedactLocked(new SrsDueItemDto(
                    s.QuestionId,
                    s.TopicId,
                    resolved.Stem,
                    resolved.Difficulty,
                    s.Status.ToString(),
                    s.NextDueAt?.UtcDateTime,
                    locked,
                    locked ? LockedContentRedactor.LOCK_REASON_PRO_REQUIRED : null));
            })
            .ToList();

        return Result.Success<IReadOnlyList<SrsDueItemDto>, Error>(items);
    }
}
