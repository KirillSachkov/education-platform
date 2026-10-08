namespace EducationContentService.Core.Features.Plans;

public sealed class AccessServiceOptions
{
    public const string SectionName = "AccessService";

    public required string Url { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromMinutes(5);
}
