using AccessService.Domain.Billing;

namespace AccessService.Core.Database;

/// <summary>
///     Репозиторий singleton-настройки приёма оплаты. Persist через
///     <see cref="ITransactionManager" /> — метода <c>SaveChangesAsync</c> на репозитории нет
///     (backend-transactions.md правило 2). Возвращает <c>null</c>, когда ряда ещё нет —
///     caller подставляет дефолт из конфига.
/// </summary>
public interface IBillingConfigRepository
{
    Task<BillingConfig?> GetAsync(CancellationToken ct = default);

    Task AddAsync(BillingConfig config, CancellationToken ct = default);
}
