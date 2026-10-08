namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record CourseCreated(Guid CourseId, Guid AuthorId);
