using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Bookmarks;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Bookmarks;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.Bookmarks.UseCases;

public sealed record CreateBookmarkCommand(Guid UserId, Guid TopicId, Guid QuestionId) : ICommand;

public sealed class CreateBookmarkCommandValidator : AbstractValidator<CreateBookmarkCommand>
{
    public CreateBookmarkCommandValidator()
    {
        RuleFor(x => x.TopicId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateBookmarkCommand.TopicId)));

        RuleFor(x => x.QuestionId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateBookmarkCommand.QuestionId)));
    }
}

public sealed class CreateBookmarkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/bookmarks",
                async Task<EndpointResult<BookmarkDto>> (
                    CreateBookmarkRequest request,
                    CreateBookmarkHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new CreateBookmarkCommand(user.UserId, request.TopicId, request.QuestionId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>Добавляет закладку вызывающего на вопрос (idempotent — повторная даёт 409).</summary>
public sealed class CreateBookmarkHandler : ICommandHandler<BookmarkDto, CreateBookmarkCommand>
{
    private readonly IValidator<CreateBookmarkCommand> _validator;
    private readonly IBookmarkedQuestionsRepository _bookmarks;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly ITransactionManager _transactions;

    public CreateBookmarkHandler(
        IValidator<CreateBookmarkCommand> validator,
        IBookmarkedQuestionsRepository bookmarks,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        ITransactionManager transactions)
    {
        _validator = validator;
        _bookmarks = bookmarks;
        _banks = banks;
        _questions = questions;
        _transactions = transactions;
    }

    public async Task<Result<BookmarkDto, Error>> Handle(
        CreateBookmarkCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Вопрос должен принадлежать банку запрошенной темы (#623, собственный банк).
        IReadOnlyList<TopicBank> topicBanks =
            await _banks.GetManyByAsync(b => b.TopicId == command.TopicId, cancellationToken);
        if (topicBanks.Count == 0)
            return TrainerServiceErrors.Topic.NoBankAvailable();

        var bankIds = topicBanks.Select(b => b.Id).ToList();
        bool questionInTopic = await _questions.ExistsAsync(
            q => q.Id == command.QuestionId && bankIds.Contains(q.BankId),
            cancellationToken);
        if (!questionInTopic)
            return TrainerServiceErrors.Question.NotFound(command.QuestionId);

        bool exists = await _bookmarks.ExistsAsync(
            b => b.UserId == command.UserId && b.QuestionId == command.QuestionId,
            cancellationToken);
        if (exists)
            return TrainerServiceErrors.Bookmark.AlreadyExists();

        BookmarkedQuestion bookmark = BookmarkedQuestion.Create(
            command.UserId,
            command.TopicId,
            command.QuestionId);
        await _bookmarks.AddAsync(bookmark, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Echo back the ref — content enrichment (stem/difficulty/topicTitle) is a list-only concern
        // (GET /trainer/bookmarks); the create response is just a confirmation of the saved bookmark.
        return new BookmarkDto(
            bookmark.Id,
            bookmark.TopicId,
            bookmark.QuestionId,
            bookmark.CreatedAt,
            Stem: null,
            Difficulty: null,
            TopicTitle: null);
    }
}
