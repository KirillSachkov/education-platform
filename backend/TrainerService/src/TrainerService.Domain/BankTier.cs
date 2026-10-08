namespace TrainerService.Domain;

/// <summary>
/// Уровень доступа банка вопросов / Question-bank access tier (фримиум-гейт).
/// FREE — открыт всем (воронка/SEO). PAID — требует плана через IEntitlementChecker.
/// </summary>
public enum BankTier
{
    FREE,
    PAID,
}
