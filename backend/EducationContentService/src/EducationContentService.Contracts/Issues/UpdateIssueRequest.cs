namespace EducationContentService.Contracts.Issues;

/// <summary>
///     Запрос на обновление задачи.
/// </summary>
public record UpdateIssueRequest(
    string Title,
    string Content,
    string AccessType,
    string SubmissionMode = "PULL_REQUEST",
    string? SelfCheckInstructions = null);
