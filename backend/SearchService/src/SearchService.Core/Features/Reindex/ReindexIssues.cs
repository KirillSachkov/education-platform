using Common;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using SearchService.Contracts;
using SearchService.Core.Database;
using SearchService.Core.Features.Reindex.IntegrationEvents;

namespace SearchService.Core.Features.Reindex;

public sealed record RequestReindexIssuesCommand : ICommand;

public sealed class ReindexIssuesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/reindex/issues", async Task<EndpointResult<SearchReindexEnqueuedResponse>> (
                [FromServices] RequestReindexIssuesHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new RequestReindexIssuesCommand(), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class RequestReindexIssuesHandler
    : ICommandHandler<SearchReindexEnqueuedResponse, RequestReindexIssuesCommand>
{
    private readonly IOutboxService _outbox;

    public RequestReindexIssuesHandler(IOutboxService outbox)
    {
        _outbox = outbox;
    }

    public async Task<Result<SearchReindexEnqueuedResponse, Error>> Handle(
        RequestReindexIssuesCommand command,
        CancellationToken cancellationToken)
    {
        Guid requestId = Guid.CreateVersion7();
        DateTime requestedAtUtc = DateTime.UtcNow;

        await _outbox.PublishAsync(new IssuesSearchReindexRequested(requestId, requestedAtUtc), cancellationToken);

        return new SearchReindexEnqueuedResponse(requestId, EntityType.Issue, requestedAtUtc);
    }
}
