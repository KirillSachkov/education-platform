namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService когда grant перешёл в EXPIRED по истечении TTL
/// (фоновая задача <c>ExpiredGrantsSweeper</c>). Семантически равнозначен
/// revoke без человеческой причины — consumer'ы снимают доступ.
/// Published by AccessService when a grant transitions to EXPIRED via TTL.
/// </summary>
/// <param name="GrantId">ID истёкшего PlanGrant.</param>
/// <param name="UserId">ID пользователя.</param>
/// <param name="PlanId">ID плана.</param>
/// <param name="PlanTier">Тип плана: <c>FREE | LEARN_ALL | FULL_ALL | COURSE | SUBSCRIPTION</c>.</param>
/// <param name="PlanAuthorId">ID автора плана-владельца. Entitlement scope определяется планом/курсами.</param>
/// <param name="CourseId">
/// Legacy / fallback: первый course ID из scope плана (для <c>COURSE</c>-плана) — нужен
/// для teardown course-tag. <c>null</c> для других тиров. Новые readers читают
/// <see cref="EffectiveCourseIds"/> (bundle-план покрывает несколько курсов, #404).
/// </param>
/// <param name="ExpiredAt">Время перехода в EXPIRED (UTC).</param>
/// <param name="CourseIds">
/// Полный список course ID'ов scope'а <c>COURSE</c>-плана (bundle, #404). <c>null</c> /
/// пустой для других тиров. Optional + trailing для backward-compat с позиционными
/// конструкторами.
/// </param>
public sealed record PlanGrantExpired(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    string PlanTier,
    Guid PlanAuthorId,
    Guid? CourseId,
    DateTimeOffset ExpiredAt,
    IReadOnlyList<Guid>? CourseIds = null,
    Guid? CanonicalTelegramPlanId = null)
{
    /// <summary>
    /// Каноничный список course ID'ов scope'а плана. Предпочитает bundle-список
    /// <see cref="CourseIds"/>; падает на legacy singular <see cref="CourseId"/>; иначе пусто.
    /// </summary>
    public IReadOnlyList<Guid> EffectiveCourseIds =>
        CourseIds is { Count: > 0 } ? CourseIds : (CourseId is { } c ? [c] : []);
}
