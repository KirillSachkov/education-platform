namespace NotificationService.Domain.Subscriptions;

/// <summary>
/// Допустимые типы сущностей для подписки / Allowed subscription entity types.
/// </summary>
public static class SubscriptionEntityType
{
    public const string COURSE = "course";
    public const string AUTHOR = "author";
    public const string MODULE = "module";
    public const int MAX_LENGTH = 16;

    /// <summary>
    /// Проверяет, является ли значение допустимым типом подписки / Checks whether the value is a valid subscription type.
    /// </summary>
    public static bool IsValid(string value) =>
        string.Equals(value, COURSE, StringComparison.Ordinal)
        || string.Equals(value, AUTHOR, StringComparison.Ordinal)
        || string.Equals(value, MODULE, StringComparison.Ordinal);
}
