namespace AuthService.Domain.ValueObjects;

public enum ConsentType
{
    /// <summary>Согласие с офертой (договором). Включает подтверждение возрастного ценза 14+ согласно п. 1 оферты.</summary>
    OFFER,

    /// <summary>Согласие на обработку персональных данных (152-ФЗ).</summary>
    PERSONAL_DATA,

    /// <summary>Согласие на маркетинговые рассылки (38-ФЗ ст. 18).</summary>
    MARKETING,

    /// <summary>Согласие на рекуррентное списание для подписки (форвард-совместимо, активируется при запуске Подписок).</summary>
    RECURRING_PAYMENT,
}
