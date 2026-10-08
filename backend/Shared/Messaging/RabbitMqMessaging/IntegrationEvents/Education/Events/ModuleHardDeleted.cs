namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record ModuleHardDeleted(
    Guid ModuleId,
    IReadOnlyList<Guid> MaterialIds);
