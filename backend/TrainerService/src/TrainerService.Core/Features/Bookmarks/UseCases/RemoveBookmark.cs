using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Bookmarks;

namespace TrainerService.Core.Features.Bookmarks.UseCases;

public sealed record RemoveBookmarkCommand(Guid UserId, Guid QuestionId) : ICommand;

public sealed class RemoveBookmarkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/trainer/bookmarks/{questionId:guid}",
                async Task<EndpointResult> (
                    Guid questionId,
                    RemoveBookmarkHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new RemoveBookmarkCommand(user.UserId, questionId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>Удаляет закладку вызывающего на вопрос.</summary>
public sealed class RemoveBookmarkHandler : ICommandHandler<RemoveBookmarkCommand>
{
    private readonly IBookmarkedQuestionsRepository _bookmarks;
    private readonly ITransactionManager _transactions;

    public RemoveBookmarkHandler(IBookmarkedQuestionsRepository bookmarks, ITransactionManager transactions)
    {
        _bookmarks = bookmarks;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        RemoveBookmarkCommand command,
        CancellationToken cancellationToken)
    {
        Result<BookmarkedQuestion, Error> bookmarkResult = await _bookmarks.GetByAsync(
            b => b.UserId == command.UserId && b.QuestionId == command.QuestionId,
            cancellationToken);
        if (bookmarkResult.IsFailure)
            return TrainerServiceErrors.Bookmark.NotFound(Guid.Empty);

        await _bookmarks.RemoveAsync(bookmarkResult.Value, cancellationToken);

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
