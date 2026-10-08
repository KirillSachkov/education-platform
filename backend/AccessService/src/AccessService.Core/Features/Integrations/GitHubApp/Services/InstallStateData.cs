namespace AccessService.Core.Features.Integrations.GitHubApp.Services;

/// <summary>
///     Per-service контекст GitHub App install-redirect state-token'а.
///     AuthorId — кто запускает install, PlanId? — опциональная страница для return-redirect'а
///     (если автор кликнул «установить» прямо со страницы редактирования плана).
///     TTL state'а — 10 min.
/// </summary>
public sealed record InstallStateData(Guid AuthorId, Guid? PlanId);
