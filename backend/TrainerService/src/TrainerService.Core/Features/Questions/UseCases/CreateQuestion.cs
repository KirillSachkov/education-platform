using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Questions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.Questions.UseCases;

public sealed record CreateQuestionCommand(Guid BankId, QuestionInputDto Input) : ICommand;

public sealed class CreateQuestionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/topic-banks/{bankId:guid}/questions",
                async Task<EndpointResult<QuestionIdResponse>> (
                    Guid bankId,
                    QuestionInputDto request,
                    CreateQuestionHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new CreateQuestionCommand(bankId, request), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Создаёт вопрос в банке (admin, #623). Проверяет, что банк существует, парсит тип/сложность,
///     валидирует доменом (choice → ≥2 варианта + правильность; EXACT_TEXT → эталон). SortKey —
///     append к существующим вопросам банка.
/// </summary>
public sealed class CreateQuestionHandler : ICommandHandler<QuestionIdResponse, CreateQuestionCommand>
{
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly TrainerFreeSampleRecomputer _freeSamples;
    private readonly ITransactionManager _transactions;

    public CreateQuestionHandler(
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        TrainerFreeSampleRecomputer freeSamples,
        ITransactionManager transactions)
    {
        _banks = banks;
        _questions = questions;
        _freeSamples = freeSamples;
        _transactions = transactions;
    }

    public async Task<Result<QuestionIdResponse, Error>> Handle(
        CreateQuestionCommand command,
        CancellationToken cancellationToken)
    {
        Result<TopicBank, Error> bankResult = await _banks.GetByAsync(b => b.Id == command.BankId, cancellationToken);
        if (bankResult.IsFailure)
            return TrainerServiceErrors.Bank.NotFound(command.BankId);

        Result<TrainerQuestionType, Error> typeResult = QuestionInputParser.ParseType(command.Input.Type);
        if (typeResult.IsFailure)
            return typeResult.Error;

        Result<QuestionDifficulty?, Error> difficultyResult = QuestionInputParser.ParseDifficulty(command.Input.Difficulty);
        if (difficultyResult.IsFailure)
            return difficultyResult.Error;

        string sortKey = await ComputeAppendSortKeyAsync(command.BankId, cancellationToken);

        Result<TrainerQuestion, Error> questionResult = TrainerQuestion.Create(
            command.BankId,
            command.Input.Stem,
            typeResult.Value,
            command.Input.ReferenceAnswer,
            command.Input.Explanation,
            difficultyResult.Value,
            command.Input.Section,
            sortKey,
            QuestionInputParser.MapOptions(command.Input.Options));
        if (questionResult.IsFailure)
            return questionResult.Error;

        await _questions.AddAsync(questionResult.Value, cancellationToken);

        // Пересчёт free-доли темы (#674) после добавления. Новый вопрос ещё не в БД — передаём его явно,
        // чтобы он попал в распределение; пересчёт и INSERT коммитятся одной транзакцией ниже.
        await _freeSamples.RecomputeForBankAsync(
            command.BankId, pendingAdd: questionResult.Value, ct: cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new QuestionIdResponse(questionResult.Value.Id);
    }

    private async Task<string> ComputeAppendSortKeyAsync(Guid bankId, CancellationToken ct)
    {
        string? maxKey = await _questions.GetMaxSortKeyAsync(bankId, ct);
        if (maxKey is null)
            return SortKey.Initial().Value;

        Result<SortKey, Error> parsed = SortKey.Create(maxKey);
        return parsed.IsSuccess ? SortKey.After(parsed.Value).Value : SortKey.Initial().Value;
    }
}
