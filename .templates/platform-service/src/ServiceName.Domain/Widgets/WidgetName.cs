namespace ServiceName.Domain.Widgets;

/// <summary>
/// Example value object. Private constructor + static `Of` factory returning
/// Result<T, Error>. Follows the platform DDD pattern.
/// </summary>
public sealed record WidgetName
{
    public const int MAX_LENGTH = 200;

    public string Value { get; }

    private WidgetName(string value) => Value = value;

    public static Result<WidgetName, Error> Of(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ServiceNameErrors.Widget.NameRequired();

        string trimmed = raw.Trim();
        if (trimmed.Length > MAX_LENGTH)
            return ServiceNameErrors.Widget.NameTooLong(MAX_LENGTH);

        return new WidgetName(trimmed);
    }

    public override string ToString() => Value;
}
