namespace AccessService.Contracts.Plans.Dtos;

/// <summary>
/// S2S-проекция плана для Telegram-бота: настроенное приветствие + имя оффера + tier.
/// Возвращается <c>GET /internal/access/plans/{planId}/telegram-info</c>. Используется
/// TelegramBotService: <see cref="WelcomeMessage"/> постится в группу при входе участника,
/// <see cref="DisplayName"/> — для именования оффера в claim-сообщении.
/// <see cref="CanonicalTelegramPlanId"/> указывает единственный план, чьи Telegram bindings
/// применимы к этому плану; для trial это канонический бессрочный FULL_ALL того же автора.
/// </summary>
public sealed record PlanTelegramInfoDto(
    Guid PlanId,
    string DisplayName,
    string Tier,
    string? WelcomeMessage,
    Guid? CanonicalTelegramPlanId = null,
    IReadOnlyList<string>? Capabilities = null);
