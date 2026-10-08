using Core.Abstractions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Quizzes.Queries;

public sealed record GetAuthorQuizByMaterialQuery(Guid MaterialId) : IQuery;

public sealed class GetAuthorQuizByMaterialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("quizzes/by-material/{materialId:guid}", async Task<EndpointResult<QuizAuthorDto>> (
                    [FromRoute] Guid materialId,
                    [FromServices] GetAuthorQuizByMaterialHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetAuthorQuizByMaterialQuery(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Авторский rediscovery квиза по материалу — ЛЮБОЙ статус (в отличие от
///     студенческого <c>GET /materials/{id}/quiz</c>, который отдаёт только
///     PUBLISHED): после reload страницы редактора автор должен снова найти свой
///     DRAFT-квиз. После инверсии Quiz↔Material (#489) резолвится через
///     <c>materials.quiz_id</c>. Проекция полная (включая ответы), поэтому только
///     владелец квиза (admin/moderator — bypass через CheckOwnership). Issue #471.
/// </summary>
public sealed class GetAuthorQuizByMaterialHandler
    : IQueryHandlerWithResult<QuizAuthorDto, GetAuthorQuizByMaterialQuery>
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly UserScopedData _userScopedData;

    public GetAuthorQuizByMaterialHandler(
        IMaterialsRepository materialsRepository,
        IQuizzesRepository quizzesRepository,
        UserScopedData userScopedData)
    {
        _materialsRepository = materialsRepository;
        _quizzesRepository = quizzesRepository;
        _userScopedData = userScopedData;
    }

    public async Task<Result<QuizAuthorDto, Error>> Handle(
        GetAuthorQuizByMaterialQuery query,
        CancellationToken cancellationToken)
    {
        Result<Material, Error> materialResult = await _materialsRepository.GetByAsync(
            m => m.Id == query.MaterialId, cancellationToken);
        if (materialResult.IsFailure || materialResult.Value.QuizId is null)
            return EducationErrors.MaterialQuizNotFound(query.MaterialId);

        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == materialResult.Value.QuizId, cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.MaterialQuizNotFound(query.MaterialId);

        Quiz quiz = quizResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(quiz.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        return QuizDtoMapper.ToAuthorDto(quiz);
    }
}
