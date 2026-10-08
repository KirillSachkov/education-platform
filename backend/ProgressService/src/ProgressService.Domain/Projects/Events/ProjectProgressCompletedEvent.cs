using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Projects.Events;

/// <summary>
/// Публикуется при фактическом завершении проекта пользователем.
/// </summary>
public sealed record ProjectProgressCompletedEvent(ProjectProgress Progress) : IDomainEvent;
