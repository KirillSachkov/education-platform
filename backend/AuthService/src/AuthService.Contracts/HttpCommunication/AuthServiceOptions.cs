namespace AuthService.Contracts.HttpCommunication;

public sealed class AuthServiceOptions
{
    public const string SECTION_NAME = nameof(AuthServiceOptions);

    public string Url { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 7;
}
