namespace TrainerService.Domain;

/// <summary>
/// Назначение банка вопросов / Question-bank purpose. STUDY — банк питает режим «Изучение»
/// (список вопросов охвата, флеш-карты, DRILL/LEARN по теме). MOCK — банк, видимый только
/// в симуляции собеса (mock-interview): не раздувает учебный список темы, но попадает в пул
/// mock-сессии. По умолчанию STUDY (обычный учебный банк).
/// </summary>
public enum BankPurpose
{
    STUDY,
    MOCK,
}
