namespace TagService.Domain.Tags;

public static class TagKindExtensions
{
    public static string ToKindString(this TagKind value) =>
        value switch
        {
            TagKind.CANON => "canon",
            TagKind.ALIAS => "alias",
            _ => "unknown"
        };
}
