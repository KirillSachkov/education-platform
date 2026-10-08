namespace ContentAccess;

public static class ResourceTypes
{
    public const string COURSE = "course";
    public const string ISSUE = "issue";
    public const string QUIZ = "quiz";
    public const string MATERIAL = "material";
    public const string COLLECTION = "collection";

    private static readonly Dictionary<string, string> _byEnumName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Course"] = COURSE,
        ["Issue"] = ISSUE,
        ["Quiz"] = QUIZ,
        ["Material"] = MATERIAL,
        ["Collection"] = COLLECTION,
    };

    public static string FromEntityType(Enum entityType) =>
        _byEnumName.TryGetValue(entityType.ToString(), out string? value)
            ? value
            : entityType.ToString().ToLowerInvariant();
}
