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

}