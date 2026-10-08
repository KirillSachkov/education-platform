using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace CommentService.Core.Features.Comments.UseCases;

/// <summary>
/// `POST /comments/author-feed/mark-viewed/` — обновляет курсор последнего просмотра ленты
/// автором. Используется фронтом при заходе на `/author/comments`, чтобы новые комменты с
/// этого момента считались «прочитанными».
/// Идемпотентно: повторный вызов просто двигает viewed_at вперёд.
/// </summary>
public sealed class MarkAuthorFeedViewedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/comments/author-feed/mark-viewed/", async Task<EndpointResult<Guid>> (
                    [FromServices] MarkAuthorFeedViewedHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new MarkAuthorFeedViewedCommand(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.WRITE);
    }
}

public sealed record MarkAuthorFeedViewedCommand : ICommand;

public sealed class MarkAuthorFeedViewedHandler : ICommandHandler<Guid, MarkAuthorFeedViewedCommand>
{
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public MarkAuthorFeedViewedHandler(ITransactionManager transactionManager, UserScopedData user)
    {
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(
        MarkAuthorFeedViewedCommand command,
        CancellationToken cancellationToken)
    {
        if (_user.UserId == Guid.Empty)
        {
            return Error.Authorization("user.not.authenticated", "Требуется аутентификация");
        }

        const string sql = """
                           INSERT INTO author_feed_state (author_id, viewed_at)
                           VALUES (@AuthorId, @ViewedAt)
                           ON CONFLICT (author_id)
                           DO UPDATE SET viewed_at = EXCLUDED.viewed_at
                           WHERE author_feed_state.viewed_at < EXCLUDED.viewed_at;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { AuthorId = _user.UserId, ViewedAt = DateTime.UtcNow },
                cancellationToken: cancellationToken));

        return _user.UserId;
    }
}
