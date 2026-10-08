using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Modules.Events;

/// <summary>
/// Публикуется при фактическом завершении модуля пользователем.
/// </summary>
public sealed record ModuleProgressCompletedEvent(ModuleProgress Progress) : IDomainEvent;
