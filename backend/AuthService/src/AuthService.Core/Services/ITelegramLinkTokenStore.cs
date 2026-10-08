namespace AuthService.Core.Services;

/// <summary>
/// Одноразовые токены привязки Telegram-аккаунта / Telegram link one-time tokens.
/// Поток:
/// <list type="number">
/// <item>Пользователь с сайта запрашивает <c>GET /users/me/telegram/link-token</c>.</item>
/// <item>Backend генерирует URL-safe токен, кладёт в Redis с TTL ~10 мин, возвращает
///       ссылку <c>https://t.me/&lt;bot&gt;?start=&lt;token&gt;</c>.</item>
/// <item>Пользователь открывает ссылку → TelegramBot получает <c>/start &lt;token&gt;</c>
///       → бот вызывает <c>POST /auth/telegram/verify</c> (service-to-service).</item>
/// <item>Backend читает токен (<see cref="PeekAsync"/>), выполняет <c>AddLoginAsync("Telegram", tgUserId)</c>
///       и удаляет токен (<see cref="DeleteAsync"/>) ТОЛЬКО после успешного commit'а — чтобы сбой/ретрай
///       после чтения не сжигал токен с незавершённой привязкой.</item>
/// </list>
/// </summary>
public interface ITelegramLinkTokenStore
{
    /// <summary>
    /// Генерирует токен, сохраняет в Redis, возвращает его. Старые токены того же пользователя
    /// не инвалидируются — юзер может запросить новую ссылку, если потерял предыдущую.
    /// </summary>
    Task<string?> GenerateAsync(Guid userId);

    /// <summary>
    /// Читает токен БЕЗ удаления, возвращает <c>UserId</c>. Null — токен не найден
    /// (истёк / уже удалён / никогда не существовал). Удаление — отдельным <see cref="DeleteAsync"/>
    /// после успешной привязки, чтобы verify был идемпотентным к ретраям бота и повторному <c>/start</c>.
    /// </summary>
    Task<Guid?> PeekAsync(string token);

    /// <summary>
    /// Удаляет токен (best-effort). Вызывается только после durable-commit привязки. Ошибка удаления
    /// не критична — TTL всё равно вычистит запись, а повторный verify пройдёт идемпотентным путём.
    /// </summary>
    Task DeleteAsync(string token);
}
