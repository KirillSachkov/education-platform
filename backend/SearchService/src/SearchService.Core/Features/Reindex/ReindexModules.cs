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

public sealed record RequestReindexModulesCommand : ICommand;

public sealed class ReindexModulesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/reindex/modules", async Task<EndpointResult<SearchReindexEnqueuedResponse>> (
                [FromServices] RequestReindexModulesHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new RequestReindexModulesCommand(), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class RequestReindexModulesHandler
    : ICommandHandler<SearchReindexEnqueuedResponse, RequestReindexModulesCommand>
{
    private readonly IOutboxService _outbox;

    public RequestReindexModulesHandler(IOutboxService outbox)
    {
        _outbox = outbox;
    }

    public async Task<Result<SearchReindexEnqueuedResponse, Error>> Handle(
        RequestReindexModulesCommand command,
        CancellationToken cancellationToken)
    {
        Guid requestId = Guid.CreateVersion7();
        DateTime requestedAtUtc = DateTime.UtcNow;

        await _outbox.PublishAsync(new ModulesSearchReindexRequested(requestId, requestedAtUtc), cancellationToken);

        return new SearchReindexEnqueuedResponse(requestId, EntityType.Module, requestedAtUtc);
    }
}
