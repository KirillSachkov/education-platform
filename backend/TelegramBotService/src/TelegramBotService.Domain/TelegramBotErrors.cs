using System.Globalization;
using SharedKernel;

namespace TelegramBotService.Domain;

public static class TelegramBotErrors
{
    public const string USER_LINK_NOT_FOUND_CODE = "telegram.user.link.not.found";
    public const string USER_LINK_ALREADY_EXISTS_CODE = "telegram.user.link.already.exists";

    public const string CHAT_BINDING_NOT_FOUND_CODE = "telegram.chat.binding.not.found";
    public const string CHAT_BINDING_ALREADY_EXISTS_CODE = "telegram.chat.binding.already.exists";
    public const string CHAT_NOT_REACHABLE_CODE = "telegram.chat.not.reachable";
    public const string CHAT_BOT_NOT_ADMIN_CODE = "telegram.chat.bot.not.admin";
    public const string CHAT_BOT_MISSING_RIGHTS_CODE = "telegram.chat.bot.missing.rights";
    public const string CHAT_INVALID_TYPE_CODE = "telegram.chat.invalid.type";

    public static Error UserLinkNotFound(long telegramUserId) =>
        Error.NotFound(
            USER_LINK_NOT_FOUND_CODE,
            $"Связь Telegram-аккаунта {telegramUserId.ToString(CultureInfo.InvariantCulture)} с платформой не найдена");

    public static Error UserLinkAlreadyExists() =>
        Error.Conflict(
            USER_LINK_ALREADY_EXISTS_CODE,
            "Связь Telegram-аккаунта с платформой уже существует");

    public static Error ChatBindingNotFound() =>
        Error.NotFound(
            CHAT_BINDING_NOT_FOUND_CODE,
            "Привязка чата к курсу не найдена");

    public static Error ChatBindingAlreadyExists() =>
        Error.Conflict(
            CHAT_BINDING_ALREADY_EXISTS_CODE,
            "Этот чат уже привязан к данному курсу");

    public static Error ChatNotReachable() =>
        Error.Validation(
            CHAT_NOT_REACHABLE_CODE,
            "Не удалось достучаться до чата. Убедитесь, что бот добавлен в чат.");

    public static Error BotNotAdmin() =>
        Error.Validation(
            CHAT_BOT_NOT_ADMIN_CODE,
            "Бот не является администратором этого чата. Назначьте бота админом и повторите.");

    public static Error BotMissingRights(string rights) =>
        Error.Validation(
            CHAT_BOT_MISSING_RIGHTS_CODE,
            $"У бота не хватает прав: {rights}. Выдайте права в настройках чата.");

    public static Error InvalidChatType(string actualType) =>
        Error.Validation(
            CHAT_INVALID_TYPE_CODE,
            $"Поддерживаются только supergroup и channel, получен: {actualType}");
}
