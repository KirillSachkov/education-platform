using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain;
using TrainerService.Domain.Questions;

namespace TrainerService.Core.Features.Questions.UseCases;

public sealed record DeleteQuestionCommand(Guid QuestionId) : ICommand;

public sealed class DeleteQuestionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/trainer/questions/{questionId:guid}",
                async Task<EndpointResult> (
                    Guid questionId,
                    DeleteQuestionHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new DeleteQuestionCommand(questionId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>Удаляет вопрос банка по id (admin, #623). Варианты каскадятся FK.</summary>
public sealed class DeleteQuestionHandler : ICommandHandler<DeleteQuestionCommand>
{
    private readonly ITrainerQuestionsRepository _questions;
    private readonly TrainerFreeSampleRecomputer _freeSamples;
    private readonly ITransactionManager _transactions;

    public DeleteQuestionHandler(
        ITrainerQuestionsRepository questions,
        TrainerFreeSampleRecomputer freeSamples,
        ITransactionManager transactions)
    {
        _questions = questions;
        _freeSamples = freeSamples;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        DeleteQuestionCommand command,
        CancellationToken cancellationToken)
    {
        Result<TrainerQuestion, Error> questionResult =
            await _questions.GetByAsync(q => q.Id == command.QuestionId, cancellationToken);
        if (questionResult.IsFailure)
            return TrainerServiceErrors.Question.NotFound(command.QuestionId);

        TrainerQuestion question = questionResult.Value;
        await _questions.RemoveAsync(question, cancellationToken);

        // Пересчёт free-доли темы (#674) с исключением удаляемого вопроса — он ещё в БД (delete
        // не сохранён), но не должен участвовать в распределении. Delete + UPDATE'ы — одна транзакция.
        await _freeSamples.RecomputeForBankAsync(
            question.BankId, removedQuestionId: question.Id, ct: cancellationToken);

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
