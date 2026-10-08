namespace AssignmentReviewService.Domain.Reviews;

/// <summary>
///     Одна попытка AI-проверки. Принадлежит <see cref="AiReview"/> через
///     <see cref="AiReviewId"/>. <see cref="IterationNumber"/> — sequential
///     начиная с 1, unique per AiReviewId (DB-level constraint).
/// </summary>
public sealed class AiReviewIteration
{
    private AiReviewIteration() { } // EF

    private AiReviewIteration(
        Guid aiReviewId,
        int iterationNumber,
        string commitSha,
        DateTimeOffset startedAt)
    {
        Id = Guid.Empty; // EF ValueGenerator
        AiReviewId = aiReviewId;
        IterationNumber = iterationNumber;
        CommitSha = commitSha;
        Status = AiReviewIterationStatus.PROCESSING;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }

    public Guid AiReviewId { get; private set; }

    public int IterationNumber { get; private set; }

    public string CommitSha { get; private set; } = string.Empty;

    public AiReviewIterationStatus Status { get; private set; }

    public AiReviewVerdict? Verdict { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public int InlineCommentsCount { get; private set; }

    public long? GitHubReviewId { get; private set; }

    public string ModelUsed { get; private set; } = string.Empty;

    public int? InputTokens { get; private set; }

    public int? OutputTokens { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>
    ///     Пути файлов репозитория, дозапрошенных моделью через need_files (#798).
    ///     <c>null</c> — дозапроса не было (или фича выключена). Хранится jsonb.
    /// </summary>
    public IReadOnlyList<string>? RequestedFiles { get; private set; }

    /// <summary>Сколько ДОПОЛНИТЕЛЬНЫХ LLM-раундов заняла итерация (#798). 0 = одношот.</summary>
    public int ContextRounds { get; private set; }

    public static AiReviewIteration Create(
        Guid aiReviewId,
        int iterationNumber,
        string commitSha)
    {
        return new AiReviewIteration(aiReviewId, iterationNumber, commitSha, DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     Iteration успешно завершилась — фиксируем результаты AI + GitHub review.
    ///     <see cref="GitHubReviewId"/> может быть null если post-review не делался
    ///     (например, no-op verdict без inline-комментариев).
    /// </summary>
    public void Complete(
        AiReviewVerdict verdict,
        string summary,
        int inlineCommentsCount,
        long? gitHubReviewId,
        string modelUsed,
        int? inputTokens,
        int? outputTokens,
        IReadOnlyList<string>? requestedFiles = null,
        int contextRounds = 0)
    {
        Status = AiReviewIterationStatus.COMPLETED;
        Verdict = verdict;
        Summary = summary ?? string.Empty;
        InlineCommentsCount = inlineCommentsCount;
        GitHubReviewId = gitHubReviewId;
        ModelUsed = modelUsed ?? string.Empty;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        RequestedFiles = requestedFiles is { Count: > 0 } ? requestedFiles.ToList() : null;
        ContextRounds = contextRounds;
        CompletedAt = DateTimeOffset.UtcNow;
        FailureReason = null;
    }

    /// <summary>
    ///     Iteration упала на одной из стадий pipeline'а. <paramref name="reason"/>
    ///     — стабильный error code (см. <c>ReviewErrors</c>); в БД сохраняется
    ///     для observability и UI lock copy.
    /// </summary>
    public void Fail(string reason, string modelUsed)
    {
        Status = AiReviewIterationStatus.FAILED;
        Verdict = null;
        ModelUsed = modelUsed ?? string.Empty;
        CompletedAt = DateTimeOffset.UtcNow;
        FailureReason = reason ?? string.Empty;
    }
}
