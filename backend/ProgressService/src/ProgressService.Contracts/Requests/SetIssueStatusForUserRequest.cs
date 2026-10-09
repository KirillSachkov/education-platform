namespace ProgressService.Contracts.Requests;

/// <summary>
///     Запрос на ручную установку ЛЮБОГО статуса прогресса задачи студенту (#518).
///     Автор/админ/модератор переключает статус из полной палитры
///     (NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / REQUESTED_CHANGES / COMPLETED), минуя обычный
///     workflow проверки. Для COMPLETED создаётся синтетический принятый submission и прогоняется
///     каскад project/module; для остальных — только переключение статуса прогресса с
///     симметричным откатом прогресса при уходе из COMPLETED.
/// </summary>
/// <param name="UserId">Студент, которому выставляется статус задачи.</param>
/// <param name="TargetStatus">
///     Целевой статус в UPPER_SNAKE_CASE — имя члена <c>IssueProgressStatus</c>
///     (NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / REQUESTED_CHANGES / COMPLETED).
/// </param>
/// <param name="ReviewerId">
///     Admin-override актора-ревьюера. Нужен только для <c>COMPLETED</c>, когда вызывающий —
///     service-токен (client_credentials, mcp-admin) с <c>sub=client_id</c> →
///     <see cref="PlatformAuth.Middleware.UserScopedData.UserId"/> = <see cref="System.Guid.Empty"/>:
///     тогда некого записать ревьюером, а <c>IssueSubmission.ForceApprove</c> реджектит пустой GUID.
///     Honored только привилегированным caller'ом (ADMIN | MODERATOR); человек из браузера имеет
///     реальный UserId из JWT и это поле игнорирует. Для не-COMPLETED статусов не требуется (нет
///     submission'а / approve). Зеркалит контракт <c>MarkIssueCompleteForUserRequest</c> (#505).
/// </param>
/// <param name="Feedback">Опциональный комментарий ревьюера (используется только для COMPLETED).</param>
public sealed record SetIssueStatusForUserRequest(
    Guid UserId,
    string TargetStatus,
    Guid? ReviewerId = null,
    string? Feedback = null);