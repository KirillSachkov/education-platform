namespace EducationContentService.Core.Features.Materials.Queries;

/// <summary>
/// Helper для query-параметра <c>accessFilter</c> на list-эндпоинтах материалов.
/// Используется и для подборок (та же семантика — отсечь ENROLLED).
/// </summary>
internal static class AccessFilterTypes
{
    /// <summary>«Бесплатное» с точки зрения витрины — всё, кроме ENROLLED. После #358 FREE-tier удалён,
    /// бесплатный = REGISTERED (system default).</summary>
    public static readonly string[] Free = ["PUBLIC", "REGISTERED"];

    /// <summary>
    /// Преобразует значение query-параметра в массив для SQL <c>ANY(@AllowedAccessTypes)</c>.
    /// Возвращает <c>null</c>, если фильтр не задан или неизвестен — handler должен
    /// рассматривать null как «без фильтра».
    /// </summary>
    public static string[]? Resolve(string? accessFilter)
    {
        if (string.IsNullOrWhiteSpace(accessFilter))
        {
            return null;
        }

        if (accessFilter.Equals("free", StringComparison.OrdinalIgnoreCase))
        {
            return Free;
        }

        return null;
    }
}
