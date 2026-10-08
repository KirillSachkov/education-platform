using Core.Abstractions;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Quizzes;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Core.Features.QuizAttempts.Queries;

public sealed record GetMyQuizAttemptsQuery(Guid QuizId) : IQuery;

public sealed class GetMyQuizAttemptsQueryValidator : AbstractValidator<GetMyQuizAttemptsQuery>
{
    public GetMyQuizAttemptsQueryValidator()
    {
        RuleFor(x => x.QuizId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetMyQuizAttemptsQuery.QuizId)));
    }
}

public sealed class GetMyQuizAttemptsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/quizzes/{quizId:guid}/attempts/my",
                async Task<EndpointResult<MyQuizAttemptsResponse>> (
                    Guid quizId,
                    [FromServices] GetMyQuizAttemptsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMyQuizAttemptsQuery(quizId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Best/last попытки текущего пользователя по квизу с per-вопрос разбором (issue #470).
///     Best — максимальный балл (tie → более поздняя), last — последняя по времени.
///     Own-data: Tier-3 entitlement-чек не нужен — ключ ответов раскрывается только
///     внутри попыток, а попытка могла появиться только через сабмит, который чек уже
///     прошёл (ревью своих прошлых попыток после revoke доступа — осознанно допустимо,
///     это учебный инструмент). Нет попыток → 200 с <c>best=null, last=null</c> без
///     обращения к ECS.
/// </summary>
public sealed class GetMyQuizAttemptsHandler : IQueryHandlerWithResult<MyQuizAttemptsResponse, GetMyQuizAttemptsQuery>
{
    private readonly IValidator<GetMyQuizAttemptsQuery> _validator;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IQuizAttemptRepository _quizAttemptRepository;
    private readonly UserScopedData _user;

    public GetMyQuizAttemptsHandler(
        IValidator<GetMyQuizAttemptsQuery> validator,
        IEducationContentServiceClient educationContentServiceClient,
        IQuizAttemptRepository quizAttemptRepository,
        UserScopedData user)
    {
        _validator = validator;
        _educationContentServiceClient = educationContentServiceClient;
        _quizAttemptRepository = quizAttemptRepository;
        _user = user;
    }

    public async Task<Result<MyQuizAttemptsResponse, Error>> Handle(
        GetMyQuizAttemptsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid userId = _user.UserId;
        Guid quizId = query.QuizId;
        (QuizAttempt? best, QuizAttempt? latest) = await _quizAttemptRepository.GetBestAndLatestAsync(
            userId,
            quizId,
            cancellationToken);

        if (best is null || latest is null)
        {
            return new MyQuizAttemptsResponse(Best: null, Last: null);
        }

        Result<QuizAnswerKeyDto, Error> answerKeyResult =
            await _educationContentServiceClient.GetQuizAnswerKeyAsync(quizId, cancellationToken);
        if (answerKeyResult.IsNotFound())
        {
            return answerKeyResult.Error;
        }

        if (answerKeyResult.IsFailure)
        {
            return ProgressErrors.EducationContentServiceUnavailable();
        }

        return new MyQuizAttemptsResponse(
            QuizAttemptGrader.BuildResult(best, answerKeyResult.Value),
            QuizAttemptGrader.BuildResult(latest, answerKeyResult.Value));
    }
}
