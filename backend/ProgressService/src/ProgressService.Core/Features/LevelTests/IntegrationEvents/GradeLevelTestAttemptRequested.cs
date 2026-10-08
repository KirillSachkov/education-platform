namespace ProgressService.Core.Features.LevelTests.IntegrationEvents;

/// <summary>
///     Internal ProgressService message: попытка level-test'а содержит открытые ответы,
///     ожидающие AI-грейдинга. Публикуется через durable outbox в той же транзакции,
///     что и сохранение попытки (статус QUEUED). Handler добавит ST-5 (#480) — он
///     прогонит open_text ответы через AI и вызовет <c>LevelTestAttempt.ApplyAiGrades</c> /
///     <c>MarkAiFailed</c>. До ST-5 подписчиков нет — Wolverine publish без маршрута
///     безопасно no-op'ится. Issue #479.
/// </summary>
public sealed record GradeLevelTestAttemptRequested(Guid AttemptId);
