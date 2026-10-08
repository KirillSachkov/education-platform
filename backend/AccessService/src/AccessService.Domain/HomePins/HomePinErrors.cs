namespace AccessService.Domain.HomePins;

/// <summary>
///     Domain errors для закрепов материалов на home-дашборде плана (epic #397).
///     Codes follow "{entity}.{condition}" convention; messages — Russian (user-facing).
/// </summary>
public static class HomePinErrors
{
    public static Error PinNotFound() =>
        Error.NotFound("home_pin.not.found", "Закреплённый материал не найден");

    public static Error MaterialNotFound() =>
        Error.NotFound("home_pin.material.not.found", "Материал не найден");

    public static Error AlreadyPinned() =>
        Error.Conflict("home_pin.already.pinned", "Этот материал уже закреплён в плане");

    public static Error NoteTooLong() =>
        Error.Validation("home_pin.note.too.long", "Заметка не должна превышать 500 символов");
}
