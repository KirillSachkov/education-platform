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

public sealed class FullReindexEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Service-to-service endpoint: reachable напрямую на порту 8009 (dev.sh, CI).
        // Через nginx не проксируется — nginx пропускает только /api/search/ → /search/.
        app.MapPost("/internal/reindex", async Task<EndpointResult<SearchReindexEnqueuedResponse>> (
                [FromServices] RequestFullReindexHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new RequestFullReindexCommand(), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);

        // Admin UI endpoint: reachable через nginx (/api/search/admin/reindex → /search/admin/reindex).
        // Только для ADMIN — обычные authors/students не могут триггерить reindex.
        app.MapPost("/search/admin/reindex", async Task<EndpointResult<SearchReindexEnqueuedResponse>> (
                [FromServices] RequestFullReindexHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new RequestFullReindexCommand(), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed record RequestFullReindexCommand : ICommand;

public sealed class RequestFullReindexHandler
    : ICommandHandler<SearchReindexEnqueuedResponse, RequestFullReindexCommand>
{
    private readonly IOutboxService _outbox;

    public RequestFullReindexHandler(IOutboxService outbox)
    {
        _outbox = outbox;
    }

    public async Task<Result<SearchReindexEnqueuedResponse, Error>> Handle(
        RequestFullReindexCommand command,
        CancellationToken cancellationToken)
    {
        Guid requestId = Guid.CreateVersion7();
        DateTime requestedAtUtc = DateTime.UtcNow;

        await _outbox.PublishAsync(
            new FullSearchReindexRequested(requestId, requestedAtUtc),
            cancellationToken);

        return new SearchReindexEnqueuedResponse(requestId, null, requestedAtUtc);
    }
}
