namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record IssueCreated(
    Guid IssueId,
    Guid ModuleId);
