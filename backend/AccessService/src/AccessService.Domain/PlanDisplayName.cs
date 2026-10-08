namespace AccessService.Domain;

/// <summary>
/// Отображаемое название плана / Plan display name.
/// </summary>
public sealed class PlanDisplayName : ValueObject
{
    public const int MAX_LENGTH = 200;

    public string Value { get; }

    private PlanDisplayName(string value) => Value = value;

    public static Result<PlanDisplayName, Error> Of(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Error.Validation("plan.name.empty", "Название плана не может быть пустым");
        }

        string trimmed = raw.Trim();

        if (trimmed.Length > MAX_LENGTH)
        {
            return Error.Validation("plan.name.too.long", $"Название длиннее {MAX_LENGTH} символов");
        }

        return new PlanDisplayName(trimmed);
    }

    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Value;
    }
}
