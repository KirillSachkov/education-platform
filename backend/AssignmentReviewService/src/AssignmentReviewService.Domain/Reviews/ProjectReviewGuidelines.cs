namespace AssignmentReviewService.Domain.Reviews;

/// <summary>
///     Денормализованный snapshot ProjectReviewContext.GuidelinesMarkdown из
///     EducationContentService для одного project'а. Обновляется handler'ом
///     <c>StoreProjectGuidelinesHandler</c> на <c>ProjectReviewContextUpdated</c>.
///     PK — <see cref="ProjectId"/> (1:0..1 с project в ECS).
/// </summary>
public sealed class ProjectReviewGuidelines : AggregateRoot
{
    private ProjectReviewGuidelines() { } // EF

    private ProjectReviewGuidelines(
        Guid projectId,
        Guid authorId,
        string guidelinesMarkdown,
        DateTimeOffset updatedAt)
    {
        Id = Guid.Empty; // EF ValueGenerator
        ProjectId = projectId;
        AuthorId = authorId;
        GuidelinesMarkdown = guidelinesMarkdown;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string GuidelinesMarkdown { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProjectReviewGuidelines Create(Guid projectId, Guid authorId, string guidelinesMarkdown)
    {
        return new ProjectReviewGuidelines(
            projectId,
            authorId,
            guidelinesMarkdown ?? string.Empty,
            DateTimeOffset.UtcNow);
    }

    public void Update(Guid authorId, string guidelinesMarkdown)
    {
        AuthorId = authorId;
        GuidelinesMarkdown = guidelinesMarkdown ?? string.Empty;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
