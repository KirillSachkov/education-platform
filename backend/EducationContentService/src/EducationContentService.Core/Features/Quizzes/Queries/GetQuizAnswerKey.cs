using Core.Abstractions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Quizzes.Queries;

public sealed record GetQuizAnswerKeyQuery(Guid QuizId) : IQuery;

public sealed class GetQuizAnswerKeyEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/quizzes/{quizId:guid}/answer-key",
                async Task<EndpointResult<QuizAnswerKeyDto>> (
                    [FromRoute] Guid quizId,
                    [FromServices] GetQuizAnswerKeyHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetQuizAnswerKeyQuery(quizId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Ключ ответов квиза для S2S-грейдинга (ProgressService).
///     Отдаёт квиз любого статуса: попытки могли быть сделаны до снятия с публикации,
///     грейдинг должен оставаться возможным. Секьюрность — как у остальных
///     <c>/internal/*</c> эндпоинтов ECS: только SERVICE / ADMIN роли.
///     Tier-3 попытки в ProgressService гейтится по самому квизу
///     (<c>QuizAnswerKeyDto.AccessType</c>) — временный MaterialId снят в ST-13 (#493).
/// </summary>
public sealed class GetQuizAnswerKeyHandler : IQueryHandlerWithResult<QuizAnswerKeyDto, GetQuizAnswerKeyQuery>
{
    private readonly IQuizzesRepository _quizzesRepository;

    public GetQuizAnswerKeyHandler(IQuizzesRepository quizzesRepository)
    {
        _quizzesRepository = quizzesRepository;
    }

    public async Task<Result<QuizAnswerKeyDto, Error>> Handle(
        GetQuizAnswerKeyQuery query,
        CancellationToken cancellationToken)
    {
        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == query.QuizId, cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.QuizNotFound(query.QuizId);

        return QuizDtoMapper.ToAnswerKeyDto(quizResult.Value);
    }
}
