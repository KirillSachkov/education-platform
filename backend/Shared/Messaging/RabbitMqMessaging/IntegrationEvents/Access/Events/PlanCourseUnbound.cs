namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService при отвязке курса от плана. Consumer'ы должны снять
/// привязку каталожной цены к курсу.
/// </summary>
/// <param name="PlanId">ID плана.</param>
/// <param name="CourseId">ID отвязанного курса.</param>
public sealed record PlanCourseUnbound(
    Guid PlanId,
    Guid CourseId);
