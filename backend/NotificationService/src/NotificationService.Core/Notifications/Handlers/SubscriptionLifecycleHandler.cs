using System.Globalization;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// Subscription lifecycle notifications. AccessService owns billing policy and publishes all
/// schedule boundaries; this consumer only formats the dates carried by each event.
/// </summary>
public sealed class SubscriptionLifecycleHandler
{
    private const string UNKNOWN_DATE = "дату уточним отдельно";

    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<SubscriptionLifecycleHandler> _logger;

    public SubscriptionLifecycleHandler(
        INotificationDispatcher dispatcher,
        ILogger<SubscriptionLifecycleHandler> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Handle(PlanGrantRenewed evt, CancellationToken ct)
    {
        string nextChargeLine = evt.NextChargeAt is { } nextChargeAt
            ? $"Следующее списание запланировано на {Format(nextChargeAt)}."
            : "Дата следующего списания появится в разделе «Мои планы».";

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.SubscriptionRenewed,
            recipientUserId: evt.UserId,
            correlationId: evt.CorrelationId,
            args: TemplateArgs.Of(
                ("expiresAt", Format(evt.ExpiresAt)),
                ("nextChargeLine", nextChargeLine)),
            payload: new
            {
                grantId = evt.GrantId,
                planId = evt.PlanId,
                renewalOrderId = evt.RenewalOrderId,
                evt.ExpiresAt,
                evt.NextChargeAt,
                evt.GraceEndsAt,
                evt.Attempt,
                evt.Stage,
            });

        await _dispatcher.DispatchAsync(request, ct);
    }

    public async Task Handle(PlanGrantRenewalFailed evt, CancellationToken ct)
    {
        if (string.Equals(
                evt.Stage,
                SubscriptionLifecycleStages.RetryScheduled,
                StringComparison.Ordinal)
            && evt.Attempt > 1)
        {
            _logger.LogInformation(
                "Skipping subscription renewal retry notification for order {OrderId} at attempt {Attempt}",
                evt.RenewalOrderId,
                evt.Attempt);
            return;
        }

        NotificationTemplate? template = evt.Stage switch
        {
            SubscriptionLifecycleStages.RetryScheduled =>
                NotificationTemplates.SubscriptionRenewalRetryScheduled,
            SubscriptionLifecycleStages.TerminalFailure =>
                NotificationTemplates.SubscriptionRenewalTerminalFailure,
            _ => null,
        };

        if (template is null)
        {
            _logger.LogWarning(
                "Ignoring subscription renewal failure {OrderId} with unknown stage {Stage}",
                evt.RenewalOrderId,
                evt.Stage);
            return;
        }

        NotificationRequest request = NotificationRequest.From(
            template: template,
            recipientUserId: evt.UserId,
            correlationId: evt.CorrelationId,
            args: TemplateArgs.Of(
                ("nextRetryAt", Format(evt.NextRetryAt)),
                ("graceEndsAt", Format(evt.GraceEndsAt))),
            payload: new
            {
                grantId = evt.GrantId,
                planId = evt.PlanId,
                renewalOrderId = evt.RenewalOrderId,
                evt.FailedAt,
                evt.NextRetryAt,
                evt.GraceEndsAt,
                evt.Attempt,
                evt.Stage,
            });

        await _dispatcher.DispatchAsync(request, ct);
    }

    public async Task Handle(PlanGrantRenewalCancelled evt, CancellationToken ct)
    {
        Guid correlationId = evt.CorrelationId == Guid.Empty ? evt.GrantId : evt.CorrelationId;

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.SubscriptionRenewalCancelled,
            recipientUserId: evt.UserId,
            correlationId: correlationId,
            args: TemplateArgs.Of(("accessEndsAt", Format(evt.AccessEndsAt))),
            payload: new
            {
                grantId = evt.GrantId,
                planId = evt.PlanId,
                renewalOrderId = evt.RenewalOrderId,
                evt.CancelledAt,
                evt.AccessEndsAt,
                evt.Attempt,
                evt.Stage,
            });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("dd.MM.yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Format(DateTimeOffset? value) =>
        value is { } date ? Format(date) : UNKNOWN_DATE;
}
