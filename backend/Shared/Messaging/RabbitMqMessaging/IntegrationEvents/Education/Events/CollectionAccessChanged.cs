namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record CollectionAccessChanged(
    Guid CollectionId,
    string AccessType,
    Guid? CourseId,
    Guid AuthorId);
