namespace AuthService.Contracts;

/// <summary>
/// S2S запрос на поиск пользователей по username/displayName/Telegram.
/// Используется AccessService'ом при ручной выдаче plan-grant'ов и ProgressService'ом
/// при резолве ростера курса по имени.
/// </summary>
public sealed record InternalUsersSearchRequest(string Query, int Limit = InternalUsersSearchRequest.DEFAULT_LIMIT)
{
    /// <summary>Дефолтный размер выборки, если вызывающий не задал лимит.</summary>
    public const int DEFAULT_LIMIT = 10;

    /// <summary>
    /// Единый потолок лимита user-search для всех вызывающих (валидатор эндпоинта +
    /// AccessService lookup + ProgressService roster). Держим в одном месте, чтобы
    /// вызывающий не мог передать значение выше потолка валидатора и получить 500/400.
    /// </summary>
    public const int MAX_LIMIT = 100;
}
