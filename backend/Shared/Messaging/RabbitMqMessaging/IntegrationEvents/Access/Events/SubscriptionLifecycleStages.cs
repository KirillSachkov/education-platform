namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Stable subscription lifecycle stage names shared by AccessService publishers and consumers.
/// Scheduling dates remain in event fields; consumers must not derive dunning policy.
/// </summary>
public static class SubscriptionLifecycleStages
{
    public const string Renewed = "RENEWED";
    public const string RetryScheduled = "RETRY_SCHEDULED";
    public const string TerminalFailure = "TERMINAL_FAILURE";
    public const string Cancelled = "CANCELLED";
    public const string Resumed = "RESUMED";
}
