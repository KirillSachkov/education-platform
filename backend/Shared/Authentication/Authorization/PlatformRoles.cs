namespace PlatformAuth.Authorization;

public static class PlatformRoles
{
    public const string PARTICIPANT = "platform-participant";
    public const string AUTHOR = "platform-author";
    public const string EDITOR = "platform-editor";
    public const string MODERATOR = "platform-moderator";
    public const string ADMIN = "platform-admin";
    public const string OWNER = "platform-owner";
    public const string SERVICE = "platform-service";

    /// <summary>User-facing roles seeded into Identity.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        PARTICIPANT, AUTHOR, EDITOR, MODERATOR, ADMIN, OWNER,
    ];
}
