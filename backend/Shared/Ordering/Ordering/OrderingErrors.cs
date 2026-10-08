using SharedKernel;

namespace Ordering;

public static class OrderingErrors
{
    public static Error InvalidSortKey(string detail) =>
        Error.Validation("ordering.sort_key.invalid", $"Невалидный ключ сортировки: {detail}");

    public static Error KeyConflict() =>
        Error.Validation("ordering.sort_key.conflict", "Невозможно вычислить позицию: нижняя граница >= верхней");
}