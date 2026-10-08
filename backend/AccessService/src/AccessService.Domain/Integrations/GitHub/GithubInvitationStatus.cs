namespace AccessService.Domain.Integrations.GitHub;

public enum GithubInvitationStatus
{
    /// <summary>Приглашение отправлено через GitHub API, ждём accept.</summary>
    PENDING,

    /// <summary>Юзер принял (либо был уже member — fast-path).</summary>
    ACCEPTED,

    /// <summary>GitHub-сторонний expire (7 дней по умолчанию).</summary>
    EXPIRED,

    /// <summary>Юзер явно отказался либо invitation отозван автором.</summary>
    CANCELED,

    /// <summary>Локальная ошибка (App не установлен, юзер не найден, token invalid).</summary>
    FAILED,
}
