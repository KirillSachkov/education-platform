using System.Text.RegularExpressions;

namespace AccessService.Domain;

/// <summary>
/// Slug плана — URL-идентификатор в платформенном каталоге.
/// Plan slug — URL identifier scoped to the platform catalog.
/// </summary>
public sealed partial class PlanSlug : ValueObject
{
    public const int MAX_LENGTH = 80;

    public string Value { get; }

    private PlanSlug(string value) => Value = value;

    public static Result<PlanSlug, Error> Of(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Error.Validation("plan.slug.empty", "Slug плана не может быть пустым");
        }

        string trimmed = raw.Trim().ToLowerInvariant();

        if (trimmed.Length > MAX_LENGTH)
        {
            return Error.Validation("plan.slug.too.long", $"Slug длиннее {MAX_LENGTH} символов");
        }

        if (!SlugRegex().IsMatch(trimmed))
        {
            return Error.Validation("plan.slug.invalid", "Slug должен содержать только латиницу, цифры и дефис");
        }

        return new PlanSlug(trimmed);
    }

    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Value;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$")]
    private static partial Regex SlugRegex();
}
