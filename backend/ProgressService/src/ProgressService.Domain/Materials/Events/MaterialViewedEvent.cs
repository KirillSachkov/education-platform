using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Materials.Events;

/// <summary>
/// Событие: пользователь впервые просмотрел материал. Raised единожды per (UserId, MaterialId).
/// Обрабатывают: <c>AwardXpOnMaterialViewed</c> (гейм-награда), <c>CompleteModuleItemOnMaterialViewed</c>
/// (cascade на module_item_progress во всех курсах, где юзер записан и содержится материал).
/// </summary>
public sealed record MaterialViewedEvent(Guid UserId, Guid MaterialId, DateTime ViewedAt) : IDomainEvent;
