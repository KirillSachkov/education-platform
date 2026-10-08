namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
/// Legacy projection текущих grant'ов пользователя для personalized home.
/// Plan access is platform/course-scoped; authorId remains only as a route compatibility hint.
/// </summary>
/// <param name="HasAnyGrant">У пользователя есть хотя бы один ACTIVE grant, подходящий под requested author context.</param>
/// <param name="HighestTier">
/// Computed tier: <c>"anonymous" | "registered" | "free" | "courses" | "lifetime"</c>.
/// Anonymous — нет JWT (никогда из этого endpoint'а — auth required).
/// Registered — авторизован, но нет подходящего grant'а.
/// Free — есть FREE-grant и нет более высокого.
/// Courses — есть COURSES-grant и нет LIFETIME_ALL.
/// Lifetime — есть FULL_ALL/LEARN_ALL grant.
/// </param>
/// <param name="Grants">ACTIVE grants пользователя для legacy author-context response.</param>
public sealed record AuthorContextDto(
    bool HasAnyGrant,
    string HighestTier,
    IReadOnlyList<PlanGrantDto> Grants);
