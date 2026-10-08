namespace AssignmentReviewService.Domain.Reviews;

/// <summary>
///     Денормализованный snapshot ReviewSpec'а из EducationContentService для одного
///     issue. Обновляется handler'ом <c>StoreIssueReviewSpecHandler</c> на каждый
///     <c>ReviewSpecUpdated</c> integration event. PK — <see cref="IssueId"/>
///     (1:0..1 с issue в ECS — logical FK через unique constraint).
///
///     Используется <c>PromptBuilder</c> для построения instructions block'а в
///     prompt'е AI-проверки. Хранение в ARS, не fetch из ECS, потому что: (а)
///     каждая run-iteration читает spec → не хочется HTTP round-trip; (б) data
///     стабильна (меняется редко), event-driven sync достаточно.
/// </summary>
public sealed class IssueReviewSpec : AggregateRoot
{
    private IssueReviewSpec() { } // EF

    private IssueReviewSpec(
        Guid issueId,
        Guid projectId,
        Guid authorId,
        string? authorPrompt,
        string? reviewAspects,
        DateTimeOffset updatedAt)
    {
        Id = Guid.Empty; // EF ValueGenerator
        IssueId = issueId;
        ProjectId = projectId;
        AuthorId = authorId;
        AuthorPrompt = authorPrompt;
        ReviewAspects = reviewAspects;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    public Guid IssueId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string? AuthorPrompt { get; private set; }

    public string? ReviewAspects { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static IssueReviewSpec Create(
        Guid issueId,
        Guid projectId,
        Guid authorId,
        string? authorPrompt,
        string? reviewAspects)
    {
        return new IssueReviewSpec(
            issueId,
            projectId,
            authorId,
            authorPrompt,
            reviewAspects,
            DateTimeOffset.UtcNow);
    }

    public void Update(
        Guid projectId,
        Guid authorId,
        string? authorPrompt,
        string? reviewAspects)
    {
        ProjectId = projectId;
        AuthorId = authorId;
        AuthorPrompt = authorPrompt;
        ReviewAspects = reviewAspects;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
