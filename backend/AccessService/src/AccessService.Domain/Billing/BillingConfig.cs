namespace AccessService.Domain.Billing;

/// <summary>
///     Singleton-настройка: включён ли на платформе приём прямой оплаты (T-Bank).
///     Один ряд с фиксированным <see cref="SingletonId" />. Если ряда ещё нет —
///     значение берётся из конфига (<c>Billing:DefaultEnabled</c>, dev=true / prod=false);
///     ряд появляется только когда админ явно переключает тумблер в админке.
///     Без domain/integration events — это рантайм admin-настройка, не доменное событие.
/// </summary>
public sealed class BillingConfig
{
    /// <summary>Фиксированный PK единственного ряда настройки.</summary>
    public static readonly Guid SingletonId = new("0b111a00-0000-0000-0000-000000000001");

    private BillingConfig() { } // EF

    private BillingConfig(Guid id, bool isEnabled, DateTimeOffset now)
    {
        Id = id;
        IsEnabled = isEnabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static BillingConfig Create(bool isEnabled, DateTimeOffset now) =>
        new(SingletonId, isEnabled, now);

    public void SetEnabled(bool isEnabled, DateTimeOffset now)
    {
        IsEnabled = isEnabled;
        UpdatedAt = now;
    }
}
