namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService после привязки курса к плану (bind или update price/state).
/// Consumer'ы (например, EducationContentService) денормализуют каталожную цену курса.
/// </summary>
/// <param name="PlanId">ID плана.</param>
/// <param name="AuthorId">ID автора плана.</param>
/// <param name="CourseId">ID курса.</param>
/// <param name="PriceCents">
///     Цена в копейках. <c>null</c> означает «цена не задана» (free-tier COURSE-план,
///     будущие промо). Consumer'ы трактуют отсутствие как «no price set», не как 0 ₽.
/// </param>
/// <param name="Currency">ISO-4217 валюта (например, <c>RUB</c>).</param>
/// <param name="IsActive">Активен ли план.</param>
/// <param name="IsPublic">Публичный ли план.</param>
public sealed record PlanCourseBound(
    Guid PlanId,
    Guid AuthorId,
    Guid CourseId,
    long? PriceCents,
    string Currency,
    bool IsActive,
    bool IsPublic);
