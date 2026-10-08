using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AuthService.Domain;

/// <summary>Доменные ошибки аутентификации.</summary>
public static class AuthErrors
{
    private static readonly Action<ILogger, string, string, Exception?> LogUnknownIdentityError =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, "UnknownIdentityError"),
            "Unknown Identity error {Code}: {Description}");

    public static Error EmailTaken() =>
        Error.Conflict("auth.email_taken", "Этот email уже используется");

    public static Error InvalidCredentials() =>
        Error.Validation("auth.invalid_credentials", "Неверный email или пароль");

    public static Error EmailNotConfirmed() =>
        Error.Validation("auth.email_not_confirmed", "Email не подтверждён");

    public static Error AccountLocked() =>
        Error.Validation("auth.account_locked", "Аккаунт заблокирован");

    public static Error InvalidOtpCode() =>
        Error.Validation("auth.invalid_otp_code", "Неверный или просроченный код");

    public static Error GitHubAuthFailed() =>
        Error.Validation("auth.github_auth_failed", "Ошибка аутентификации через GitHub");

    public static Error GitHubEmailRequired() =>
        Error.Validation("auth.github_email_required", "У GitHub аккаунта должен быть публичный email");

    public static Error ExternalLoginInfoMissing() =>
        Error.Validation("auth.external_login_info_missing", "Данные внешнего входа отсутствуют");

    public static Error FromIdentityError(IdentityError error, ILogger? logger = null) => error.Code switch
    {
        "PasswordTooShort"                => Error.Validation("auth.password.too_short", "Пароль должен содержать минимум 8 символов"),
        "PasswordRequiresUpper"           => Error.Validation("auth.password.requires_uppercase", "Пароль должен содержать заглавную букву"),
        "PasswordRequiresLower"           => Error.Validation("auth.password.requires_lowercase", "Пароль должен содержать строчную букву"),
        "PasswordRequiresDigit"           => Error.Validation("auth.password.requires_digit", "Пароль должен содержать цифру"),
        "PasswordRequiresNonAlphanumeric" => Error.Validation("auth.password.requires_special", "Пароль должен содержать спецсимвол"),
        "DuplicateEmail"                  => EmailTaken(),
        "DuplicateUserName"               => Error.Conflict("auth.username_taken", "Это имя пользователя уже занято"),
        "InvalidUserName"                 => Error.Validation("auth.invalid_username", "Имя пользователя содержит недопустимые символы"),
        _                                 => LogAndReturnGenericError(error, logger),
    };

    private static Error LogAndReturnGenericError(IdentityError error, ILogger? logger)
    {
        if (logger is not null)
            LogUnknownIdentityError(logger, error.Code, error.Description, null);

        return Error.Validation("auth.identity_error", "Произошла ошибка при обработке запроса");
    }

    public static Error PasswordAlreadySet() =>
        Error.Conflict("auth.password_already_set", "Пароль уже установлен");

    public static Error InvalidResetToken() =>
        Error.Validation("auth.invalid_reset_token", "Ссылка для сброса пароля недействительна или просрочена");

    public static Error GitHubNotLinked() =>
        Error.Validation("auth.github_not_linked", "GitHub аккаунт не привязан");

    public static Error UserNotFound() =>
        Error.NotFound("auth.user_not_found", "Пользователь не найден");

    public static Error CannotModifySelf() =>
        Error.Validation("auth.cannot_modify_self", "Нельзя изменить собственный аккаунт");

    public static Error InvalidRole(string role) =>
        Error.Validation("auth.invalid_role", $"Недопустимая роль: {role}");

    public static Error TooManyAttempts() =>
        Error.Validation("auth.too_many_attempts", "Слишком много попыток, попробуйте позже");

    public static Error MandatoryConsentMissing() =>
        Error.Validation(
            "auth.consent.mandatory.missing",
            "Необходимо принять оферту и согласие на обработку персональных данных для регистрации");

    public static Error TelegramLinkTokenExpired() =>
        Error.Validation("telegram.link.token_expired", "Ссылка привязки устарела или уже использована");

    public static Error TelegramAlreadyLinkedToOther() =>
        Error.Conflict("telegram.link.already_linked_to_other", "Этот Telegram-аккаунт уже привязан к другому пользователю платформы");

    public static Error TelegramLinkFailed() =>
        Error.Failure("telegram.link.failed", "Не удалось привязать Telegram-аккаунт");

    public static Error TelegramUnlinkFailed() =>
        Error.Failure("telegram.unlink.failed", "Не удалось отвязать Telegram-аккаунт");

    public static Error TelegramLinkTokenGenerationFailed() =>
        Error.Failure("telegram.link.token_generation_failed", "Не удалось создать ссылку для привязки Telegram");

    public static Error TelegramLinkNotConfigured() =>
        Error.Failure("telegram.link.not_configured", "Привязка Telegram недоступна");
}
