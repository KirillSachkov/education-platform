using Core.Abstractions;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.WebPush.Requests;
using NotificationService.Core.Database;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.WebPush.UseCases;

/// <summary>
/// DELETE /notifications/push/subscriptions — отписывает устройство по endpoint'у.
/// Идемпотентно (удаление отсутствующей подписки = success). Auth required. Issue #342.
/// </summary>
public sealed class RemoveWebPushSubscriptionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/notifications/push/subscriptions", async Task<EndpointResult<string>> (
                [FromBody] RemoveWebPushSubscriptionRequest request,
                [FromServices] RemoveWebPushSubscriptionHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new RemoveWebPushSubscriptionCommand(request), cancellationToken))
            .RequireAuthorization()
            .RequireRateLimiting(NotificationRateLimitPolicies.PUSH_REGISTER);
    }
}

public sealed class RemoveWebPushSubscriptionValidator
    : AbstractValidator<RemoveWebPushSubscriptionCommand>
{
    public RemoveWebPushSubscriptionValidator()
    {
        RuleFor(x => x.Request.Endpoint)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("web_push.endpoint"));
    }
}

public sealed record RemoveWebPushSubscriptionCommand(RemoveWebPushSubscriptionRequest Request) : ICommand;

public sealed class RemoveWebPushSubscriptionHandler
    : ICommandHandler<string, RemoveWebPushSubscriptionCommand>
{
    private readonly IWebPushSubscriptionsRepository _subscriptions;
    private readonly UserScopedData _user;
    private readonly IValidator<RemoveWebPushSubscriptionCommand> _validator;

    public RemoveWebPushSubscriptionHandler(
        IWebPushSubscriptionsRepository subscriptions,
        UserScopedData user,
        IValidator<RemoveWebPushSubscriptionCommand> validator)
    {
        _subscriptions = subscriptions;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<string, Error>> Handle(
        RemoveWebPushSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Scoped к user_id — нельзя удалить чужую подписку, даже зная endpoint. Идемпотентно.
        await _subscriptions.RemoveByEndpointAsync(_user.UserId, command.Request.Endpoint, cancellationToken);

        return command.Request.Endpoint;
    }
}
