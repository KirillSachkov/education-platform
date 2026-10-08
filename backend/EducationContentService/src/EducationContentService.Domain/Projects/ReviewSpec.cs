namespace EducationContentService.Domain.Projects;

/// <summary>
///     Aggregate root: спецификация AI-проверки для конкретного задания
///     (1:0..1 с <see cref="Issue"/>). Содержит prompt автора и нюансы проверки.
/// </summary>
public sealed class ReviewSpec
{
    public const int AUTHOR_PROMPT_MAX_LENGTH = 10_000;
    public const int REVIEW_ASPECTS_MAX_LENGTH = 10_000;

    private ReviewSpec() { } // EF

    private ReviewSpec(
        Guid issueId,
        Guid projectId,
        string? authorPrompt,
        string? reviewAspects,
        bool isAutoReviewEnabled,
        DateTime createdAt)
    {
        Id = Guid.CreateVersion7();
        IssueId = issueId;
        ProjectId = projectId;
        AuthorPrompt = authorPrompt;
        ReviewAspects = reviewAspects;
        IsAutoReviewEnabled = isAutoReviewEnabled;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid IssueId { get; private set; }

    /// <summary>Denorm для запросов: project containing this issue.</summary>
    public Guid ProjectId { get; private set; }

    public string? AuthorPrompt { get; private set; }

    public string? ReviewAspects { get; private set; }

    /// <summary>Per-issue override (default true). false ⇒ кнопка «Запустить» скрыта.</summary>
    public bool IsAutoReviewEnabled { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static ReviewSpec Create(
        Guid issueId,
        Guid projectId,
        string? authorPrompt,
        string? reviewAspects,
        bool isAutoReviewEnabled = true)
    {
        return new ReviewSpec(
            issueId,
            projectId,
            authorPrompt,
            reviewAspects,
            isAutoReviewEnabled,
            DateTime.UtcNow);
    }

    public void Update(
        string? authorPrompt,
        string? reviewAspects,
        bool isAutoReviewEnabled)
    {
        AuthorPrompt = authorPrompt;
        ReviewAspects = reviewAspects;
        IsAutoReviewEnabled = isAutoReviewEnabled;
        UpdatedAt = DateTime.UtcNow;
    }
}
