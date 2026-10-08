namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
///     Сводка состояния доступа текущего пользователя для платформенной модалки «доступ
///     истёк» (#687). <see cref="RecentlyExpired"/> непустой, если у юзера недавно истёк
///     срочный grant и сейчас нет активного grant'а, покрывающего тот же scope.
/// </summary>
public sealed record AccessStatusDto(
    bool HasActiveAccess,
    ExpiredAccessDto? RecentlyExpired);

/// <summary>
///     Недавно истёкший доступ — питает текст модалки и CTA «продлить» (ведёт на
///     <c>/pricing</c>, где доплата уже учитывает оплаченное через upgrade-credit).
/// </summary>
public sealed record ExpiredAccessDto(
    Guid GrantId,
    Guid PlanId,
    string PlanName,
    string Tier,
    DateTimeOffset ExpiredAt);
