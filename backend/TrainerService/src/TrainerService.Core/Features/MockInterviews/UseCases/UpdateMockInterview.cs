using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.MockInterviews;

namespace TrainerService.Core.Features.MockInterviews.UseCases;

public sealed record UpdateMockInterviewCommand(
    Guid MockInterviewId,
    string? Title,
    string? Description,
    int? QuestionsPerSession,
    IReadOnlyList<MockInterviewQuestionRefDto> Questions) : ICommand;

public sealed class UpdateMockInterviewCommandValidator : AbstractValidator<UpdateMockInterviewCommand>
{
    public UpdateMockInterviewCommandValidator()
    {
        RuleFor(x => x.MockInterviewId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateMockInterviewCommand.MockInterviewId)));

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateMockInterviewCommand.Title)));
    }
}

public sealed class UpdateMockInterviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/trainer/mock-interviews/{mockInterviewId:guid}",
                async Task<EndpointResult<MockInterviewIdResponse>> (
                    Guid mockInterviewId,
                    UpdateMockInterviewRequest request,
                    UpdateMockInterviewHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new UpdateMockInterviewCommand(
                            mockInterviewId,
                            request.Title,
                            request.Description,
                            request.QuestionsPerSession,
                            request.Questions ?? []),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Обновляет симуляцию собеседования (admin): название/описание + размер случайной подвыборки на
///     сессию + курированный набор вопросов (полная замена). Снапшот стемов не делается — вопросы
///     резолвятся из ECS при старте сессии / в редакторе. #585.
/// </summary>
public sealed class UpdateMockInterviewHandler : ICommandHandler<MockInterviewIdResponse, UpdateMockInterviewCommand>
{
    private readonly IValidator<UpdateMockInterviewCommand> _validator;
    private readonly IMockInterviewsRepository _mockInterviews;
    private readonly ITransactionManager _transactions;

    public UpdateMockInterviewHandler(
        IValidator<UpdateMockInterviewCommand> validator,
        IMockInterviewsRepository mockInterviews,
        ITransactionManager transactions)
    {
        _validator = validator;
        _mockInterviews = mockInterviews;
        _transactions = transactions;
    }

    public async Task<Result<MockInterviewIdResponse, Error>> Handle(
        UpdateMockInterviewCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<MockInterview, Error> interviewResult =
            await _mockInterviews.GetByAsync(m => m.Id == command.MockInterviewId, cancellationToken);
        if (interviewResult.IsFailure)
            return TrainerServiceErrors.MockInterview.NotFound(command.MockInterviewId);

        MockInterview interview = interviewResult.Value;

        UnitResult<Error> detailsResult = interview.UpdateDetails(command.Title, command.Description);
        if (detailsResult.IsFailure)
            return detailsResult.Error;

        UnitResult<Error> perSessionResult = interview.SetQuestionsPerSession(command.QuestionsPerSession);
        if (perSessionResult.IsFailure)
            return perSessionResult.Error;

        interview.SetQuestions(command.Questions
            .Select(q => q.QuestionId)
            .ToList());

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new MockInterviewIdResponse(interview.Id);
    }
}
