namespace AccessService.Core.Features.Plans;

/// <summary>
///     Конфиг плана доступа. Биндится на секцию <c>"Access"</c>.
/// </summary>
public sealed class AccessOptions
{
    public const string SECTION_NAME = "Access";

    /// <summary>
    ///     Длительность пробного доступа в днях. Срок задаётся платформой (config),
    ///     автор не вводит его при создании trial-плана (#595). Default — 30.
    /// </summary>
    public int TrialDurationDays { get; set; } = 30;

    /// <summary>
    ///     Полный доступ к платформе (грант <c>FULL_ALL</c>) автоматически даёт Trainer Pro (#568):
    ///     при выдаче/пересчёте тегов FULL_ALL-грант доливает capability-тег <c>cap:TRAINER_PRO</c>,
    ///     даже хотя capability не входит в <c>FULL</c> (это подписочный add-on). Настраиваемо: выкл →
    ///     Pro только по платной подписке Trainer Pro. Default — <c>true</c>. После смены флага на
    ///     проде нужен ре-синк тегов (<c>backfill-redis-from-grants</c>).
    /// </summary>
    public bool FullPlatformGrantsTrainerPro { get; set; } = true;
}
