namespace ProgressService.Contracts.Requests;

/// <summary>
///     Запрос на ручную приёмку задачи студенту, который никогда не сдавал работу (#398).
///     Автор/админ/модератор отмечает задание выполненным — создаётся синтетический принятый
///     submission и прогоняется обычный каскад XP/project/module.
/// </summary>
/// <param name="UserId">Студент, которому засчитывается выполнение задачи.</param>
/// <param name="Feedback">Опциональный комментарий ревьюера.</param>
/// <param name="ReviewerId">
///     Admin-override актора-ревьюера. Нужен, когда вызывающий — service-токен (client_credentials,
///     mcp-admin) с <c>sub=client_id</c> → <see cref="PlatformAuth.Middleware.UserScopedData.UserId"/>
///     = <see cref="System.Guid.Empty"/>: тогда некого записать в <c>ReviewerId</c>, а
///     <c>IssueSubmission.ForceApprove</c> реджектит пустой GUID. Honored только привилегированным
///     caller'ом (ADMIN | MODERATOR); человек-автор/админ из браузера имеет реальный UserId из JWT
///     и это поле игнорирует. Зеркалит <c>MaterialProcessingService.RequestedBy</c>.
/// </param>
public sealed record MarkIssueCompleteForUserRequest(Guid UserId, string? Feedback = null, Guid? ReviewerId = null);
