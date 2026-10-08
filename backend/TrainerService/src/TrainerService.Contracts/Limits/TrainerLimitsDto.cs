namespace TrainerService.Contracts.Limits;

/// <summary>
///     Лимиты AI-использования вызывающего + статус Trainer Pro (#568) — для карточки «лимиты»
///     в правом блоке хаба. Каждое измерение несёт «использовано из лимита»; <c>Limit=null</c> ⇒
///     безлимит (PRO без потолка / admin). <c>OpenGrades</c>/<c>Mock</c> — в штуках; <c>Voice</c> —
///     в МИНУТАХ аудио (#663). Free-юзер видит свой дневной лимит грейдов, а голос/мок у него
///     <c>0</c> (PRO-фича) — карточка показывает это как «нужен Pro».
/// </summary>
public sealed record TrainerLimitsDto(
    bool IsPro,
    TrainerLimitDto OpenGrades,
    TrainerLimitDto Voice,
    TrainerLimitDto Mock);

/// <summary>
///     Один лимит: сколько использовано и каков потолок периода. <c>Limit=null</c> ⇒ безлимит.
///     Единица зависит от измерения: штуки для open-grade/mock, МИНУТЫ аудио для voice (#663).
/// </summary>
public sealed record TrainerLimitDto(int Used, int? Limit);
