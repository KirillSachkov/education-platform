using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.MockInterviews;

namespace TrainerService.Core.Features.MockInterviews.UseCases;

public sealed record PublishMockInterviewCommand(Guid MockInterviewId) : ICommand;

public sealed class PublishMockInterviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/mock-interviews/{mockInterviewId:guid}/publish",
                async Task<EndpointResult<MockInterviewIdResponse>> (
                    Guid mockInterviewId,
                    PublishMockInterviewHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new PublishMockInterviewCommand(mockInterviewId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Публикует симуляцию собеседования (admin) — становится видна участникам в хабе. Требует хотя бы
///     один источник вопросов (курированный набор ИЛИ темы). Идемпотентно: повтор на уже опубликованной
///     симуляции — no-op, успех. #585.
/// </summary>
public sealed class PublishMockInterviewHandler : ICommandHandler<MockInterviewIdResponse, PublishMockInterviewCommand>
{
    private readonly IMockInterviewsRepository _mockInterviews;
    private readonly ITransactionManager _transactions;

    public PublishMockInterviewHandler(
        IMockInterviewsRepository mockInterviews,
        ITransactionManager transactions)
    {
        _mockInterviews = mockInterviews;
        _transactions = transactions;
    }

    public async Task<Result<MockInterviewIdResponse, Error>> Handle(
        PublishMockInterviewCommand command,
        CancellationToken cancellationToken)
    {
        Result<MockInterview, Error> interviewResult =
            await _mockInterviews.GetByAsync(m => m.Id == command.MockInterviewId, cancellationToken);
        if (interviewResult.IsFailure)
            return TrainerServiceErrors.MockInterview.NotFound(command.MockInterviewId);

        MockInterview interview = interviewResult.Value;

        UnitResult<Error> publishResult = interview.Publish();
        if (publishResult.IsFailure)
            return publishResult.Error;

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new MockInterviewIdResponse(interview.Id);
    }
}
