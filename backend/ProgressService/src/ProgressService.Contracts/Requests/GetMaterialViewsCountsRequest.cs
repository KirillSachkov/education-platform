namespace ProgressService.Contracts.Requests;

/// <summary>
///     Batch-запрос на счётчики просмотров материалов. Используется ECS при рендере
///     карточек/детали для бейджа «N просмотров». Анонимный эндпоинт.
/// </summary>
public sealed record GetMaterialViewsCountsRequest(IReadOnlyCollection<Guid> MaterialIds);
