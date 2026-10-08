namespace ServiceName.Domain;

/// <summary>
/// Domain error factories. Codes follow "{entity}.{condition}" convention.
/// Messages are Russian by convention (user-facing via API Envelope.message).
/// </summary>
public static class ServiceNameErrors
{
    public static class Widget
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("widget.not.found", $"Widget {id} не найден.");

        public static Error NameTooLong(int max) =>
            Error.Validation("widget.name.too.long", $"Название не может быть длиннее {max} символов.");

        public static Error NameRequired() =>
            Error.Validation("widget.name.required", "Название обязательно.");
    }
}
