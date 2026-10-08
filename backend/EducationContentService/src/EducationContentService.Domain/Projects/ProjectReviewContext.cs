namespace EducationContentService.Domain.Projects;

/// <summary>
///     Aggregate root: контекст AI-проверки для проекта (1:0..1 с <see cref="Project"/>).
///     Хранит PROJECT-level guidelines автора — plain markdown, который
///     AssignmentReviewService кладёт в prompt ревьюера как контекст (issue #15).
///     Ref-repo + RAG-индексация удалены в #320.
/// </summary>
public sealed class ProjectReviewContext
{
    public const int GUIDELINES_MAX_LENGTH = 50_000;

    private ProjectReviewContext() { } // EF

    private ProjectReviewContext(
        Guid projectId,
        string guidelinesMarkdown,
        bool isAutoReviewEnabled,
        bool requiresGithubConnection,
        bool requiresReviewApp,
        DateTime createdAt)
    {
        Id = Guid.CreateVersion7();
        ProjectId = projectId;
        GuidelinesMarkdown = guidelinesMarkdown;
        IsAutoReviewEnabled = isAutoReviewEnabled;
        RequiresGithubConnection = requiresGithubConnection;
        RequiresReviewApp = requiresReviewApp;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string GuidelinesMarkdown { get; private set; } = string.Empty;

    /// <summary>Глобальный switch авто-AI-проверки на проект (default true).</summary>
    public bool IsAutoReviewEnabled { get; private set; }

    /// <summary>Нужно ли студенту привязать GitHub-профиль перед PR-сдачей проекта.</summary>
    public bool RequiresGithubConnection { get; private set; }

    /// <summary>Нужно ли студенту установить GitHub App / review bot перед PR-сдачей проекта.</summary>
    public bool RequiresReviewApp { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static ProjectReviewContext Create(
        Guid projectId,
        string guidelinesMarkdown,
        bool isAutoReviewEnabled = true,
        bool requiresGithubConnection = true,
        bool requiresReviewApp = true)
    {
        return new ProjectReviewContext(
            projectId,
            guidelinesMarkdown ?? string.Empty,
            isAutoReviewEnabled,
            requiresGithubConnection,
            requiresReviewApp,
            DateTime.UtcNow);
    }

    public void Update(
        string guidelinesMarkdown,
        bool isAutoReviewEnabled,
        bool requiresGithubConnection = true,
        bool requiresReviewApp = true)
    {
        GuidelinesMarkdown = guidelinesMarkdown ?? string.Empty;
        IsAutoReviewEnabled = isAutoReviewEnabled;
        RequiresGithubConnection = requiresGithubConnection;
        RequiresReviewApp = requiresReviewApp;
        UpdatedAt = DateTime.UtcNow;
    }
}
