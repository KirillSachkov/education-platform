namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record MaterialAccessChanged(
    Guid MaterialId,
    string AccessType,
    IReadOnlyList<Guid> CourseIds,
    Guid AuthorId);
