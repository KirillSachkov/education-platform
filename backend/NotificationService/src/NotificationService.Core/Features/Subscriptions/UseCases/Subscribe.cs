using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Subscriptions.Dtos;
using NotificationService.Contracts.Subscriptions.Requests;
using NotificationService.Core.Database;
using NotificationService.Domain.Subscriptions;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.Subscriptions.UseCases;

public sealed class SubscribeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/subscriptions", async Task<EndpointResult<SubscriptionDto>> (
                [FromBody] SubscribeRequest request,
                [FromServices] SubscribeHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new SubscribeCommand(request), cancellationToken))
            .RequireAuthorization();
    }
}

public sealed class SubscribeValidator : AbstractValidator<SubscribeCommand>
{
    public SubscribeValidator()
    {
        RuleFor(x => x.Request.EntityType)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("subscription.entity.type"))
            .Must(SubscriptionEntityType.IsValid)
            .WithError(Error.Validation(
                "subscription.entity.type.invalid",
                "Недопустимый тип подписки"));

        RuleFor(x => x.Request.EntityId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("subscription.entity.id"));
    }
}

public sealed record SubscribeCommand(SubscribeRequest Request) : ICommand;

public sealed class SubscribeHandler : ICommandHandler<SubscriptionDto, SubscribeCommand>
{
    private readonly ISubscriptionsRepository _subscriptionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<SubscribeCommand> _validator;

    public SubscribeHandler(
        ISubscriptionsRepository subscriptionsRepository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<SubscribeCommand> validator)
    {
        _subscriptionsRepository = subscriptionsRepository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<SubscriptionDto, Error>> Handle(
        SubscribeCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        string entityType = command.Request.EntityType;
        Guid entityId = command.Request.EntityId;
        Guid userId = _user.UserId;

        // Idempotent: одним запросом проверяем существование и возвращаем DTO существующей
        // подписки (раньше было ExistsBy + ListBy всех подписок юзера c TOCTOU — issue #230, ARCH-1).
        Subscription? existing = await _subscriptionsRepository.GetBy(
            x => x.UserId == userId && x.EntityType == entityType && x.EntityId == entityId,
            cancellationToken);

        if (existing is not null)
        {
            return new SubscriptionDto
            {
                Id = existing.Id.Value,
                EntityType = existing.EntityType,
                EntityId = existing.EntityId,
                CreatedAt = new DateTimeOffset(existing.CreatedAt, TimeSpan.Zero),
            };
        }

        Result<Subscription, Error> subscriptionResult = Subscription.Create(userId, entityType, entityId);

        if (subscriptionResult.IsFailure)
            return subscriptionResult.Error;

        Subscription subscription = subscriptionResult.Value;

        await _subscriptionsRepository.AddAsync(subscription, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);

        if (saveResult.IsFailure)
            return saveResult.Error;

        return new SubscriptionDto
        {
            Id = subscription.Id.Value,
            EntityType = subscription.EntityType,
            EntityId = subscription.EntityId,
            CreatedAt = new DateTimeOffset(subscription.CreatedAt, TimeSpan.Zero),
        };
    }
}
