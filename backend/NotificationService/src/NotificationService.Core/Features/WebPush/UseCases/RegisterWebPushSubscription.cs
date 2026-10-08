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
using NotificationService.Domain.WebPush;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.WebPush.UseCases;

/// <summary>
/// POST /notifications/push/subscriptions — регистрирует web-push подписку устройства.
/// Идемпотентно (upsert по endpoint'у). Auth required. Issue #342.
/// </summary>
public sealed class RegisterWebPushSubscriptionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/push/subscriptions", async Task<EndpointResult<string>> (
                [FromBody] RegisterWebPushSubscriptionRequest request,
                [FromServices] RegisterWebPushSubscriptionHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new RegisterWebPushSubscriptionCommand(request), cancellationToken))
            .RequireAuthorization()
            .RequireRateLimiting(NotificationRateLimitPolicies.PUSH_REGISTER);
    }
}

public sealed class RegisterWebPushSubscriptionValidator
    : AbstractValidator<RegisterWebPushSubscriptionCommand>
{
    public RegisterWebPushSubscriptionValidator()
    {
        RuleFor(x => x.Request.Endpoint)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("web_push.endpoint"))
            .MaximumLength(2048)
            .WithError(Error.Validation("web_push.endpoint.too_long", "Слишком длинный endpoint"));

        RuleFor(x => x.Request.P256dh)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("web_push.p256dh"))
            .MaximumLength(256)
            .WithError(Error.Validation("web_push.p256dh.too_long", "Слишком длинный ключ p256dh"));

        RuleFor(x => x.Request.Auth)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("web_push.auth"))
            .MaximumLength(256)
            .WithError(Error.Validation("web_push.auth.too_long", "Слишком длинный auth-секрет"));

        RuleFor(x => x.Request.UserAgent)
            .MaximumLength(512)
            .WithError(Error.Validation("web_push.user_agent.too_long", "Слишком длинный User-Agent"))
            .When(x => x.Request.UserAgent is not null);
    }
}

public sealed record RegisterWebPushSubscriptionCommand(RegisterWebPushSubscriptionRequest Request) : ICommand;

public sealed class RegisterWebPushSubscriptionHandler
    : ICommandHandler<string, RegisterWebPushSubscriptionCommand>
{
    private const int MaxDevicesPerUser = 20;

    private readonly IWebPushSubscriptionsRepository _subscriptions;
    private readonly UserScopedData _user;
    private readonly IValidator<RegisterWebPushSubscriptionCommand> _validator;

    public RegisterWebPushSubscriptionHandler(
        IWebPushSubscriptionsRepository subscriptions,
        UserScopedData user,
        IValidator<RegisterWebPushSubscriptionCommand> validator)
    {
        _subscriptions = subscriptions;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<string, Error>> Handle(
        RegisterWebPushSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        RegisterWebPushSubscriptionRequest request = command.Request;

        Result<WebPushSubscription, Error> created = WebPushSubscription.Create(
            _user.UserId, request.Endpoint, request.P256dh, request.Auth, request.UserAgent);

        if (created.IsFailure)
            return created.Error;

        // Per-user device cap — ограничивает fan-out доставки (WebPushNotificationChannel
        // итерирует все подписки юзера на каждое уведомление). Ре-регистрация уже известного
        // endpoint'а под cap не попадает.
        IReadOnlyList<WebPushSubscription> existing =
            await _subscriptions.GetByUserIdAsync(_user.UserId, cancellationToken);
        bool alreadyRegistered = existing.Any(s =>
            string.Equals(s.Endpoint, request.Endpoint, StringComparison.Ordinal));
        if (!alreadyRegistered && existing.Count >= MaxDevicesPerUser)
        {
            return Error.Validation(
                "web_push.too_many_devices",
                "Слишком много устройств с push-уведомлениями. Отключите push на одном из старых.");
        }

        // Идемпотентный upsert по endpoint'у — ON CONFLICT DO UPDATE (см. repository).
        await _subscriptions.UpsertAsync(created.Value, cancellationToken);

        return request.Endpoint;
    }
}
