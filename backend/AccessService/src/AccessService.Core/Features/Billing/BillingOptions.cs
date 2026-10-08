namespace AccessService.Core.Features.Billing;

public sealed class BillingOptions
{
    public const string SECTION_NAME = "Billing";

    /// <summary>
    ///     Дефолт приёма оплаты, когда в БД ещё нет явной admin-настройки.
    ///     dev/docker = true, prod = false (см. appsettings.*). Админ переопределяет
    ///     тумблером в /admin/payments — переопределение живёт в <c>billing_config</c>.
    /// </summary>
    public bool DefaultEnabled { get; set; }
}
