using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.UseCases;

public sealed class AdminGrantEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/grants/admin/", async Task<EndpointResult<PlanGrantDto>> (
                [FromBody] AdminGrantRequest request,
                [FromServices] AdminGrantHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new AdminGrantCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Plans.GRANT);
    }
}

public sealed record AdminGrantCommand(AdminGrantRequest Request) : ICommand;

public sealed class AdminGrantHandler : ICommandHandler<PlanGrantDto, AdminGrantCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<AdminGrantHandler> _logger;

    public AdminGrantHandler(
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<AdminGrantHandler> logger)
    {
        _plans = plans;
        _grants = grants;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<PlanGrantDto, Error>> Handle(
        AdminGrantCommand command,
        CancellationToken cancellationToken)
    {
        AdminGrantRequest request = command.Request;

        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == request.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return getPlan.Error;
        }

        Plan plan = getPlan.Value;

        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        if (plan.ArchivedAt is not null)
        {
            return AccessErrors.PlanArchived();
        }

        Guid recipientId = request.UserId;
        Result<PlanGrant, Error> existing = await _grants.GetByAsync(
            g => g.UserId == recipientId
                 && g.PlanId == plan.Id
                 && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return PlanGrantMapper.MapToDto(existing.Value);
        }

        PlanGrant grant = PlanGrant.Create(
            recipientId,
            plan.Id,
            PlanGrantSource.ADMIN_GRANT,
            sourceRef: _user.UserId,
            expiresAt: request.ExpiresAt);

        await _grants.AddAsync(grant, cancellationToken);

        await _outbox.PublishAsync(new PlanGrantCreated(
            grant.Id,
            grant.UserId,
            plan.Id,
            plan.Tier.ToString(),
            plan.AuthorId,
            plan.FirstCourseId,
            plan.IncludesFutureContent,
            grant.Source.ToString(),
            grant.SourceRef,
            grant.GrantedAt,
            grant.ExpiresAt,
            PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
            [.. plan.Courses.Select(c => c.CourseId)],
            plan.DisplayName.Value,
            plan.OfferType.ToString()));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Admin grant {GrantId} issued to user {UserId} on plan {PlanId} by {IssuerId}",
            grant.Id, recipientId, plan.Id, _user.UserId);

        return PlanGrantMapper.MapToDto(grant);
    }
}
