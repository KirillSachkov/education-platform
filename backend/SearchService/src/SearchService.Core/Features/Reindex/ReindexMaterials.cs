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

public sealed record RequestReindexMaterialsCommand : ICommand;

public sealed class ReindexMaterialsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/reindex/materials", async Task<EndpointResult<SearchReindexEnqueuedResponse>> (
                [FromServices] RequestReindexMaterialsHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new RequestReindexMaterialsCommand(), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class RequestReindexMaterialsHandler
    : ICommandHandler<SearchReindexEnqueuedResponse, RequestReindexMaterialsCommand>
{
    private readonly IOutboxService _outbox;

    public RequestReindexMaterialsHandler(IOutboxService outbox)
    {
        _outbox = outbox;
    }

    public async Task<Result<SearchReindexEnqueuedResponse, Error>> Handle(
        RequestReindexMaterialsCommand command,
        CancellationToken cancellationToken)
    {
        Guid requestId = Guid.CreateVersion7();
        DateTime requestedAtUtc = DateTime.UtcNow;

        await _outbox.PublishAsync(new MaterialsSearchReindexRequested(requestId, requestedAtUtc), cancellationToken);

        return new SearchReindexEnqueuedResponse(requestId, EntityType.Material, requestedAtUtc);
    }
}
