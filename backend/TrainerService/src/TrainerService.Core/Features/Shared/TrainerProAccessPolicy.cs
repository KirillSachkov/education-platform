using ContentAccess;

namespace TrainerService.Core.Features.Shared;

/// <summary>
///     Резолвит, есть ли у пользователя подписка тренажёра (capability <c>cap:TRAINER_PRO</c>),
///     которая открывает PAID-банки, голосовые ответы и мок-собесы. Бесплатный tier — FREE-банки
///     + текстовые ответы. Один Redis SINTER на запрос; не размазывать сырой <c>HasCapabilityAsync</c>
///     по хендлерам — звать только через этот хелпер.
///     <para>
///         Admin/Author bypass обеспечивается самим <see cref="IEntitlementChecker.HasCapabilityAsync"/>
///         (<c>subject.IsAdmin → true</c>), но <paramref name="isAdmin"/> также короткозамыкается здесь,
///         чтобы admin не зависел от Redis. Capability-имя — <c>"TRAINER_PRO"</c> (без префикса
///         <c>cap:</c>, его добавляет checker через <see cref="GrantTags.Capability"/>).
///     </para>
/// </summary>
public static class TrainerProAccessPolicy
{
    /// <summary>Capability-имя add-on'а тренажёра (Plan.Capabilities → Redis тег <c>cap:TRAINER_PRO</c>).</summary>
    public const string CAPABILITY_NAME = "TRAINER_PRO";

    /// <summary>
    ///     Машинно-читаемая причина замка для фронта (paywall-копирайт): тема/действие требует
    ///     PRO-подписки. Кладётся в <c>LockReason</c> DTO рядом с <c>IsLocked=true</c>.
    /// </summary>
    public const string LOCK_REASON_PRO_REQUIRED = "pro_required";

    /// <summary>
    ///     <c>true</c>, если пользователь admin ИЛИ держатель <c>cap:TRAINER_PRO</c>. Fail-closed:
    ///     при недоступности Redis checker возвращает <c>false</c> (см. <c>ResilientEntitlementChecker</c>).
    /// </summary>
    public static async Task<bool> HasProAsync(
        Guid userId,
        bool isAdmin,
        IEntitlementChecker entitlementChecker,
        CancellationToken ct = default)
    {
        if (isAdmin)
            return true;

        AccessSubject subject = new(
            IsAuthenticated: userId != Guid.Empty,
            UserId: userId,
            IsAdmin: false);

        return await entitlementChecker.HasCapabilityAsync(subject, CAPABILITY_NAME, ct);
    }
}
