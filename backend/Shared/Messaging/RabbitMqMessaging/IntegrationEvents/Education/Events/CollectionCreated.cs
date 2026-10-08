namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record CollectionCreated(
    Guid CollectionId,
    string AccessType,
    Guid? CourseId,
    Guid AuthorId);
