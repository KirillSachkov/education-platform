namespace EducationContentService.Contracts.HttpCommunication;

/// <summary>
///     Настройки подключения к EducationContentService.
/// </summary>
public record EducationServiceOptions
{
    public string Url { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 7;
}