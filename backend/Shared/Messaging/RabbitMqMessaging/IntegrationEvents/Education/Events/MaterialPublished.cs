namespace Shared.Messaging.IntegrationEvents.Education.Events;

/// <summary>
/// Published when a material transitions to the Published state.
/// Rich payload: всё, что нужно NotificationService для рендера уведомления подписчикам курса,
/// без дополнительных HTTP-вызовов в EducationContentService.
/// </summary>
/// <param name="CourseIds">Список курсов, к которым привязан материал; может быть пуст (orphan).</param>
/// <param name="NotifySubscribers">
/// Если <c>true</c> (дефолт), NotificationService рассылает уведомление подписчикам.
/// Автор может снять галочку при публикации, чтобы не спамить (например, мелкая правка).
/// </param>
public sealed record MaterialPublished(
    Guid MaterialId,
    string Title,
    Guid AuthorId,
    IReadOnlyList<Guid> CourseIds,
    bool NotifySubscribers = true);
