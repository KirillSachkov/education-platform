using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Questions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain;
using TrainerService.Domain.Questions;

namespace TrainerService.Core.Features.Questions.UseCases;

public sealed record UpdateQuestionCommand(Guid QuestionId, QuestionInputDto Input) : ICommand;

public sealed class UpdateQuestionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/trainer/questions/{questionId:guid}",
                async Task<EndpointResult<QuestionIdResponse>> (
                    Guid questionId,
                    QuestionInputDto request,
                    UpdateQuestionHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new UpdateQuestionCommand(questionId, request), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Обновляет вопрос банка (admin, #623): стем/тип/эталон/разбор/сложность/секция + варианты
///     (полная замена). BankId/SortKey immutable. Доменная валидация по типу.
/// </summary>
public sealed class UpdateQuestionHandler : ICommandHandler<QuestionIdResponse, UpdateQuestionCommand>
{
    private readonly ITrainerQuestionsRepository _questions;
    private readonly TrainerFreeSampleRecomputer _freeSamples;
    private readonly ITransactionManager _transactions;

    public UpdateQuestionHandler(
        ITrainerQuestionsRepository questions,
        TrainerFreeSampleRecomputer freeSamples,
        ITransactionManager transactions)
    {
        _questions = questions;
        _freeSamples = freeSamples;
        _transactions = transactions;
    }

    public async Task<Result<QuestionIdResponse, Error>> Handle(
        UpdateQuestionCommand command,
        CancellationToken cancellationToken)
    {
        Result<TrainerQuestion, Error> questionResult =
            await _questions.GetByAsync(q => q.Id == command.QuestionId, cancellationToken);
        if (questionResult.IsFailure)
            return TrainerServiceErrors.Question.NotFound(command.QuestionId);

        Result<TrainerQuestionType, Error> typeResult = QuestionInputParser.ParseType(command.Input.Type);
        if (typeResult.IsFailure)
            return typeResult.Error;

        Result<QuestionDifficulty?, Error> difficultyResult = QuestionInputParser.ParseDifficulty(command.Input.Difficulty);
        if (difficultyResult.IsFailure)
            return difficultyResult.Error;

        TrainerQuestion question = questionResult.Value;
        UnitResult<Error> updateResult = question.Update(
            command.Input.Stem,
            typeResult.Value,
            command.Input.ReferenceAnswer,
            command.Input.Explanation,
            difficultyResult.Value,
            command.Input.Section,
            QuestionInputParser.MapOptions(command.Input.Options));
        if (updateResult.IsFailure)
            return updateResult.Error;

        // Тип/сложность могли измениться → пересчитываем free-долю темы (#674). Изменённый вопрос уже
        // в БД и трекается с новыми значениями; пересчёт + UPDATE'ы коммитятся одной транзакцией.
        await _freeSamples.RecomputeForBankAsync(question.BankId, ct: cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new QuestionIdResponse(question.Id);
    }
}
