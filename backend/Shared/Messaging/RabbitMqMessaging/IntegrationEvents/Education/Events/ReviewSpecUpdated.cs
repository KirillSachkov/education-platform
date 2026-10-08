namespace Shared.Messaging.IntegrationEvents.Education.Events;

/// <summary>
///     Опубликован при создании / обновлении <c>ReviewSpec</c> в EducationContentService.
///     Consumer: AssignmentReviewService snapshot'ит в <c>issue_review_specs</c> для
///     PromptBuilder'а (issue #15, #320).
/// </summary>
public sealed record ReviewSpecUpdated(
    Guid IssueId,
    Guid ProjectId,
    Guid AuthorId,
    string? AuthorPrompt,
    string? ReviewAspects);
