using System.Diagnostics.Metrics;
using Observability;

namespace AccessService.Core.Diagnostics;

/// <summary>
///     Бизнес-метрики plan-onboarding pipeline. Singleton + IMeterFactory согласно
///     правилу из root CLAUDE.md ("точечно, не разводи static helpers").
///     Имя meter'а — <see cref="ObservabilityExtensions.ONBOARDING_METER"/>.
///
///     Покрываем:
///     <list type="bullet">
///     <item><b>onboarding_invitation_outcomes_total</b> — counter per (action, outcome).
///         Action = `create` | `sync`. Outcome = `created` | `already_member` | `failed_*`
///         | `accepted` | `not_member`.</item>
///     <item><b>onboarding_github_api_duration_seconds</b> — histogram per endpoint.
///         Помогает увидеть задержки GitHub API (token_acquisition, invite, get_user, membership).</item>
///     <item><b>onboarding_webhook_signature_total</b> — counter (`valid` | `invalid` | `missing`).
///         Persistent invalid → security incident или разъехавшийся секрет.</item>
///     <item><b>onboarding_webhook_event_total</b> — counter per event type
///         (installation/created, organization/member_added, etc.).</item>
///     <item><b>onboarding_token_cache_total</b> — counter (`hit` | `miss` | `invalidated`).
///         Низкий hit-ratio = что-то не так с TTL кеша.</item>
///     <item><b>onboarding_state_store_total</b> — counter (`set` | `consume_hit` | `consume_miss`).
///         consume_miss = expired token или brute-force attempt.</item>
///     </list>
/// </summary>
public sealed class OnboardingMetrics
{
    private readonly Counter<long> _invitationOutcomes;
    private readonly Histogram<double> _githubApiDuration;
    private readonly Counter<long> _webhookSignature;
    private readonly Counter<long> _webhookEvent;
    private readonly Counter<long> _tokenCache;
    private readonly Counter<long> _stateStore;

    public OnboardingMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ObservabilityExtensions.ONBOARDING_METER);

        _invitationOutcomes = meter.CreateCounter<long>(
            name: "onboarding_invitation_outcomes_total",
            unit: "{outcome}",
            description: "GitHub org invitation outcomes per action (create / sync) and result.");

        _githubApiDuration = meter.CreateHistogram<double>(
            name: "onboarding_github_api_duration_seconds",
            unit: "s",
            description: "Latency of GitHub API calls per endpoint.");

        _webhookSignature = meter.CreateCounter<long>(
            name: "onboarding_webhook_signature_total",
            unit: "{result}",
            description: "GitHub webhook signature verification: valid / invalid / missing.");

        _webhookEvent = meter.CreateCounter<long>(
            name: "onboarding_webhook_event_total",
            unit: "{event}",
            description: "GitHub webhook events received and processed per event-name.");

        _tokenCache = meter.CreateCounter<long>(
            name: "onboarding_token_cache_total",
            unit: "{outcome}",
            description: "Installation token cache hit / miss / invalidated.");

        _stateStore = meter.CreateCounter<long>(
            name: "onboarding_state_store_total",
            unit: "{outcome}",
            description: "Install state store: set / consume_hit / consume_miss.");
    }

    public void RecordInvitationOutcome(string action, string outcome) =>
        _invitationOutcomes.Add(1,
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordGitHubApi(string endpoint, TimeSpan elapsed, string outcome) =>
        _githubApiDuration.Record(elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordWebhookSignature(string result) =>
        _webhookSignature.Add(1, new KeyValuePair<string, object?>("result", result));

    public void RecordWebhookEvent(string eventName, string action) =>
        _webhookEvent.Add(1,
            new KeyValuePair<string, object?>("event", eventName),
            new KeyValuePair<string, object?>("action", action));

    public void RecordTokenCache(string outcome) =>
        _tokenCache.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordStateStore(string outcome) =>
        _stateStore.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
