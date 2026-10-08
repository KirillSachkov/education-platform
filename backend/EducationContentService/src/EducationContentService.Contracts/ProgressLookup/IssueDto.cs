namespace EducationContentService.Contracts.ProgressLookup;

/// <summary>
///     Легковесный DTO задачи для сервиса прогресса (service-to-service).
/// </summary>
public sealed record IssueDto(
    Guid? ModuleId,
    string SubmissionMode = "PULL_REQUEST",
    string? SelfCheckInstructions = null,
    bool RequiresGithubConnection = true,
    bool RequiresReviewApp = true,
    bool IsAutoReviewEnabled = true);
