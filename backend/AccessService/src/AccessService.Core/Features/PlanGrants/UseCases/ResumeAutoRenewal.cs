using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AccessService.Core.Features.PlanGrants.UseCases;

public sealed class ResumeAutoRenewalEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/me/grants/{grantId:guid}/resume-renewal/",
                async Task<EndpointResult<PlanGrantDto>> (
                    [FromRoute] Guid grantId,
                    [FromServices] ResumeAutoRenewalHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new ResumeAutoRenewalCommand(grantId), ct))
            .RequireAuthorization();
    }
}

public sealed record ResumeAutoRenewalCommand(Guid GrantId) : ICommand;

public sealed class ResumeAutoRenewalHandler
    : ICommandHandler<PlanGrantDto, ResumeAutoRenewalCommand>
{
    public Task<Result<PlanGrantDto, Error>> Handle(
        ResumeAutoRenewalCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<PlanGrantDto, Error>>(AccessErrors.SalesClosed());
}