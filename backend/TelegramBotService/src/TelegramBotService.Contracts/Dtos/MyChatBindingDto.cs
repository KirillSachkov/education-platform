namespace TelegramBotService.Contracts.Dtos;

/// <summary>
///     Привязка курса к TG-чату с точки зрения пользователя — ему нужны invite-link
///     и контекст «к каким курсам относится». Используется в /telegram/me/chats и в
///     UI «Мои чаты» / на странице курса.
///
///     <para>Дедуп по <see cref="TelegramChatId"/>: если один чат привязан к нескольким курсам,
///     юзер видит одну карточку, а <see cref="PlanIds"/> агрегирует все курсы пользователя,
///     которые шарят этот чат. <see cref="PlanTitles"/> index-aligned с <see cref="PlanIds"/> —
///     обогащается на бэке через ECS, чтобы фронт не делал N+1 на <c>/courses/{id}/detail</c>
///     (тот endpoint требует entitlement-проверку и отдаёт 403 студентам без STANDARD-зачисления).</para>
///
///     <see cref="IsMember"/> отмечает, состоит ли юзер уже в чате (через
///     <c>ChatMembershipChecker</c>). Фронт по этому флагу выбирает label кнопки
///     («Войти в чат» vs «Открыть чат»).
/// </summary>
public sealed record MyChatBindingDto(
    IReadOnlyList<Guid> PlanIds,
    IReadOnlyList<string> PlanTitles,
    long TelegramChatId,
    string ChatType,
    string? ChatTitle,
    string InviteLink,
    bool IsMember);
