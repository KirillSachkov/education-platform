namespace Shared.Messaging.IntegrationEvents.Education.Events;

/// <summary>
///     Опубликован при создании / обновлении <c>ProjectReviewContext</c> в EducationContentService.
///     Consumer: AssignmentReviewService — snapshot'ит guidelines markdown как PROJECT-level
///     context для prompt'а ревьюера (issue #15). Ref-repo / RAG-индексация удалена в #320.
/// </summary>
public sealed record ProjectReviewContextUpdated(
    Guid ProjectId,
    Guid AuthorId,
    string GuidelinesMarkdown);
