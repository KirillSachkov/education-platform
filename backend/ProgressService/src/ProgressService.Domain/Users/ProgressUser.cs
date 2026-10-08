namespace ProgressService.Domain.Users;

public sealed class ProgressUser
{
    private ProgressUser(Guid userId, string? username, string? displayName)
    {
        UserId = userId;
        Username = username;
        DisplayName = displayName;
    }

    private ProgressUser()
    {
    }

    public Guid UserId { get; private set; }

    public string? Username { get; private set; }

    public string? DisplayName { get; private set; }

    public static Result<ProgressUser, Error> Create(Guid userId, string? username, string? displayName = null)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        return new ProgressUser(userId, username, displayName);
    }

    public void UpdateUsername(string? username)
    {
        Username = username;
    }

    public void UpdateDisplayName(string? displayName)
    {
        DisplayName = displayName;
    }
}
