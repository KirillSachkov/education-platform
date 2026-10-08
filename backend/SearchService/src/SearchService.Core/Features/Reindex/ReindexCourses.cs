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

public sealed record RequestReindexCoursesCommand : ICommand;

public sealed class ReindexCoursesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/reindex/courses", async Task<EndpointResult<SearchReindexEnqueuedResponse>> (
                [FromServices] RequestReindexCoursesHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new RequestReindexCoursesCommand(), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class RequestReindexCoursesHandler
    : ICommandHandler<SearchReindexEnqueuedResponse, RequestReindexCoursesCommand>
{
    private readonly IOutboxService _outbox;

    public RequestReindexCoursesHandler(IOutboxService outbox)
    {
        _outbox = outbox;
    }

    public async Task<Result<SearchReindexEnqueuedResponse, Error>> Handle(
        RequestReindexCoursesCommand command,
        CancellationToken cancellationToken)
    {
        Guid requestId = Guid.CreateVersion7();
        DateTime requestedAtUtc = DateTime.UtcNow;

        await _outbox.PublishAsync(new CoursesSearchReindexRequested(requestId, requestedAtUtc), cancellationToken);

        return new SearchReindexEnqueuedResponse(requestId, EntityType.Course, requestedAtUtc);
    }
}
