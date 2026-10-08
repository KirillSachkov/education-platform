namespace AccessService.Contracts.HttpCommunication;

public sealed class AccessServiceOptions
{
    public const string SECTION_NAME = nameof(AccessServiceOptions);

    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// Per-request timeout. Default 3s — низкий, потому что caller-flow (GH auto-enroll)
    /// идёт после уже коммитнутого CourseEnrollment'а: AccessService grant — это
    /// non-critical enrichment, fallback на legacy course-tags не сломает доступ
    /// при долгом ответе. Не путать с Polly retry policy ниже.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 3;
}
