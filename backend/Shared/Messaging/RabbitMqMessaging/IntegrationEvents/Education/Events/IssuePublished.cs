namespace Shared.Messaging.IntegrationEvents.Education.Events;

/// <summary>
/// Published when an issue transitions to the Published state.
/// Rich payload: всё, что нужно NotificationService для рендера уведомления подписчикам курса,
/// без дополнительных HTTP-вызовов в EducationContentService.
/// </summary>
/// <param name="CourseIds">Список курсов, к которым привязан issue через project; может быть пуст.</param>
/// <param name="NotifySubscribers">
/// Если <c>true</c> (дефолт), NotificationService рассылает уведомление подписчикам.
/// Автор может снять галочку при публикации, чтобы не спамить.
/// </param>
public sealed record IssuePublished(
    Guid IssueId,
    Guid ProjectId,
    string Title = "",
    Guid AuthorId = default,
    IReadOnlyList<Guid>? CourseIds = null,
    bool NotifySubscribers = true);
