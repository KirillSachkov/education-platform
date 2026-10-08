using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Questions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.Questions.Queries;

public sealed record GetBankQuestionsQuery(Guid BankId) : IQuery;

public sealed class GetBankQuestionsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/topic-banks/{bankId:guid}/questions",
                async Task<EndpointResult<IReadOnlyList<QuestionAdminDto>>> (
                    Guid bankId,
                    GetBankQuestionsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetBankQuestionsQuery(bankId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Admin-список ПОЛНЫХ вопросов банка для редактора (#623): включает варианты с признаком
///     правильности + эталон + разбор + sortKey, упорядочен по SortKey. Только role ADMIN —
///     студентам ответы не утекают.
/// </summary>
public sealed class GetBankQuestionsHandler
    : IQueryHandlerWithResult<IReadOnlyList<QuestionAdminDto>, GetBankQuestionsQuery>
{
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;

    public GetBankQuestionsHandler(ITopicBanksRepository banks, ITrainerQuestionsRepository questions)
    {
        _banks = banks;
        _questions = questions;
    }

    public async Task<Result<IReadOnlyList<QuestionAdminDto>, Error>> Handle(
        GetBankQuestionsQuery query,
        CancellationToken cancellationToken)
    {
        Result<TopicBank, Error> bankResult = await _banks.GetByAsync(b => b.Id == query.BankId, cancellationToken);
        if (bankResult.IsFailure)
            return TrainerServiceErrors.Bank.NotFound(query.BankId);

        IReadOnlyList<TrainerQuestion> questions =
            await _questions.GetManyByAsync(q => q.BankId == query.BankId, cancellationToken);

        return questions
            .OrderBy(q => q.SortKey, StringComparer.Ordinal)
            .Select(q => new QuestionAdminDto(
                q.Id,
                q.BankId,
                q.Stem,
                q.Type.ToString(),
                q.ReferenceAnswer,
                q.Explanation,
                q.Difficulty?.ToString(),
                q.Section,
                q.SortKey,
                q.Options
                    .OrderBy(o => o.SortIndex)
                    .Select(o => new QuestionOptionAdminDto(o.Id, o.Text, o.IsCorrect, o.SortIndex))
                    .ToList(),
                q.CreatedAt,
                q.UpdatedAt))
            .ToList();
    }
}
