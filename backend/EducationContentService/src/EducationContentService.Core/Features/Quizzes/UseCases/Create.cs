using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Quizzes.UseCases;

public sealed record CreateQuizCommand(CreateQuizRequest Request) : ICommand;

public sealed class CreateQuizRequestValidator : AbstractValidator<CreateQuizRequest>
{
    public CreateQuizRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);

        RuleFor(x => x.PassingScorePercent)
            .InclusiveBetween(0, 100)
            .WithError(EducationErrors.InvalidQuizPassingScore());

        RuleFor(x => x.Questions!)
            .Must(q => q.Count <= Quiz.MAX_QUESTIONS)
            .WithError(EducationErrors.QuizQuestionsLimitExceeded(Quiz.MAX_QUESTIONS))
            .When(x => x.Questions is not null);

        RuleFor(x => x.AccessType!)
            .Must(value => Enum.TryParse<AccessType>(value, ignoreCase: true, out _))
            .WithError(EducationErrors.InvalidAccessType())
            .When(x => !string.IsNullOrWhiteSpace(x.AccessType));
    }
}

public sealed class CreateQuizEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("quizzes", async Task<EndpointResult<Guid>> (
                    [FromBody] CreateQuizRequest request,
                    [FromServices] CreateQuizHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new CreateQuizCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Создаёт standalone-квиз в DRAFT (#489). Привязка к материалу — на стороне
///     материала: <c>PATCH /materials/{id}</c> с <c>QuizId</c> (один квиз может
///     переиспользоваться несколькими материалами). AccessType — собственный уровень
///     доступа квиза (<c>null</c> в запросе → PUBLIC).
/// </summary>
public sealed class CreateQuizHandler : ICommandHandler<Guid, CreateQuizCommand>
{
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<CreateQuizRequest> _validator;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<CreateQuizHandler> _logger;

    public CreateQuizHandler(
        IQuizzesRepository quizzesRepository,
        ITransactionManager transactionManager,
        IValidator<CreateQuizRequest> validator,
        UserScopedData userScopedData,
        ILogger<CreateQuizHandler> logger)
    {
        _quizzesRepository = quizzesRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateQuizCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Title title = Title.Create(command.Request.Title).Value;

        QuizPurpose purpose = QuizPurpose.MATERIAL_CHECK;
        if (!string.IsNullOrWhiteSpace(command.Request.Purpose)
            && !Enum.TryParse(command.Request.Purpose, ignoreCase: true, out purpose))
        {
            return EducationErrors.InvalidQuizPurpose(command.Request.Purpose);
        }

        AccessType accessType = string.IsNullOrWhiteSpace(command.Request.AccessType)
            ? AccessType.PUBLIC
            : Enum.Parse<AccessType>(command.Request.AccessType, ignoreCase: true);

        Result<List<QuizQuestion>, Error> questionsResult = QuizQuestionMapper.Map(command.Request.Questions);
        if (questionsResult.IsFailure)
            return questionsResult.Error;

        Result<LevelTestConfig?, Error> configResult = QuizLevelTestConfigMapper.Map(command.Request.LevelTestConfig);
        if (configResult.IsFailure)
            return configResult.Error;

        bool titleConflict = await _quizzesRepository.ExistsByTitleAsync(
            title, excludeId: null, cancellationToken);
        if (titleConflict)
            return EducationErrors.TitleAlreadyExists("Quiz", title.Value);

        // Admin-only override: служебные client_credentials caller'ы (MCP-сценарий)
        // явно передают AuthorId, потому что у service-токена sub=client_id → UserId=Guid.Empty.
        // Для не-admin caller'а поле игнорируется — author всегда берётся из request scope.
        Guid quizAuthorId =
            _userScopedData.IsAdmin && command.Request.AuthorId is { } overrideAuthorId
                ? overrideAuthorId
                : _userScopedData.UserId;

        Result<Quiz, Error> quizResult = Quiz.Create(
            quizAuthorId,
            title,
            questionsResult.Value,
            command.Request.PassingScorePercent,
            purpose,
            configResult.Value,
            accessType);
        if (quizResult.IsFailure)
            return quizResult.Error;

        Quiz quiz = quizResult.Value;

        await _quizzesRepository.AddAsync(quiz, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Quiz {QuizId} created by {AuthorId} (AccessType={AccessType}, Questions={QuestionCount}, PassingScore={PassingScore})",
            quiz.Id, quiz.AuthorId, quiz.AccessType, quiz.Questions.Count, quiz.PassingScorePercent);

        return quiz.Id;
    }
}
