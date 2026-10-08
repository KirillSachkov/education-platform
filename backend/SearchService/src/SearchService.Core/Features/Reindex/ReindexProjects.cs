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

public sealed record RequestReindexProjectsCommand : ICommand;

public sealed class ReindexProjectsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/reindex/projects", async Task<EndpointResult<SearchReindexEnqueuedResponse>> (
                [FromServices] RequestReindexProjectsHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new RequestReindexProjectsCommand(), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class RequestReindexProjectsHandler
    : ICommandHandler<SearchReindexEnqueuedResponse, RequestReindexProjectsCommand>
{
    private readonly IOutboxService _outbox;

    public RequestReindexProjectsHandler(IOutboxService outbox)
    {
        _outbox = outbox;
    }

    public async Task<Result<SearchReindexEnqueuedResponse, Error>> Handle(
        RequestReindexProjectsCommand command,
        CancellationToken cancellationToken)
    {
        Guid requestId = Guid.CreateVersion7();
        DateTime requestedAtUtc = DateTime.UtcNow;

        await _outbox.PublishAsync(new ProjectsSearchReindexRequested(requestId, requestedAtUtc), cancellationToken);

        return new SearchReindexEnqueuedResponse(requestId, EntityType.Project, requestedAtUtc);
    }
}
