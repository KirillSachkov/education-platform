namespace CommentService.Web.Configuration;

public sealed class CommentRateLimitOptions
{
    public const string SECTION_NAME = "CommentRateLimit";

    public int CreatePermitLimit { get; init; } = 10;
    public int CreateWindowMinutes { get; init; } = 1;
}
