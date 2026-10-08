using MaterialProcessingService.Domain.AiSettings;

namespace MaterialProcessingService.Core.Repositories;

public interface IAiModelSettingsRepository
{
    /// <summary>
    ///     Возвращает singleton-row или <c>null</c>, если ещё не было ни одного админ-сохранения.
    /// </summary>
    Task<AiModelSettings?> GetAsync(bool asNoTracking = false, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Добавляет новый singleton-row. Используется когда handler уже определил что
    ///     <see cref="GetAsync"/> вернул null — повторная проверка существования здесь не делается.
    ///     Update existing tracked entity делается через aggregate-методы (e.g. <c>UpdateAll</c>).
    /// </summary>
    Task AddAsync(AiModelSettings settings, CancellationToken cancellationToken = default);
}
