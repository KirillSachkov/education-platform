namespace AssignmentReviewService.Core.Features.Installations.Services;

/// <summary>
///     Per-service контекст GitHub App install-redirect state-token'а для ARS.
///     UserId — кто запустил install (student / author), ReturnUrl? — куда вернуться
///     после успешного callback'а (валидируется на старте как относительный путь).
///     TTL state'а — 10 min, single-use.
/// </summary>
public sealed record InstallStateData(Guid UserId, string? ReturnUrl);
