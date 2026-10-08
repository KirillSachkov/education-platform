using AssignmentReviewService.Domain.Vcs;

namespace AssignmentReviewService.Domain.Reviews;

/// <summary>
///     Aggregate root: одна AI-проверка для student submission. 1:1 с
///     <c>progress.issue_submissions</c> через <see cref="SubmissionId"/>
///     (logical FK — разные сервисы / разные schemas, без physical FK).
/// </summary>
public sealed class AiReview : AggregateRoot
{
    private readonly List<AiReviewIteration> _iterations = [];

    private AiReview() { } // EF

    private AiReview(
        Guid submissionId,
        Guid issueId,
        Guid userId,
        Guid authorId,
        VcsProvider provider,
        string repoFullName,
        int pullNumber,
        string pullRequestUrl,
        DateTimeOffset createdAt)
    {
        Id = Guid.Empty; // EF ValueGenerator
        SubmissionId = submissionId;
        IssueId = issueId;
        UserId = userId;
        AuthorId = authorId;
        Provider = provider;
        RepoFullName = repoFullName;
        PullNumber = pullNumber;
        PullRequestUrl = pullRequestUrl;
        Status = AiReviewStatus.QUEUED;
        IterationsCount = 0;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid SubmissionId { get; private set; }

    public Guid IssueId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>
    ///     Автор курса (через issue → project → author). Денорм-snapshot из
    ///     <c>IssueSubmissionAwaitingReview</c> event payload — используется в
    ///     downstream integration events / audit, в pipeline reviewer'у не передаётся.
    /// </summary>
    public Guid AuthorId { get; private set; }

    public VcsProvider Provider { get; private set; }

    public string RepoFullName { get; private set; } = string.Empty;

    public int PullNumber { get; private set; }

    public string PullRequestUrl { get; private set; } = string.Empty;

    public AiReviewStatus Status { get; private set; }

    public Guid? LatestIterationId { get; private set; }

    public int IterationsCount { get; private set; }

    public AiReviewVerdict? LatestVerdict { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    ///     Liveness-метка прогресса длинного RUNNING-ревью (#690). Bump'ится после
    ///     каждого batch'а в <c>RunIteration</c>/<c>AiReviewer</c> через raw-SQL
    ///     <see cref="Database.IAiReviewsRepository.HeartbeatRunningAsync"/> — НЕ трогает
    ///     <see cref="UpdatedAt"/> (lease-fencing token), поэтому running-lease остаётся
    ///     валидным. Stale-watchdog считает RUNNING-ревью зависшим по
    ///     <c>GREATEST(updated_at, heartbeat_at)</c>, поэтому легитимно-долгое multi-batch
    ///     ревью (которое дольше порога, но прогрессирует) не переотправляется и не
    ///     starve'ится. <c>null</c> до первого heartbeat'а — тогда watchdog смотрит на
    ///     <see cref="UpdatedAt"/>.
    /// </summary>
    public DateTimeOffset? HeartbeatAt { get; private set; }

    public IReadOnlyCollection<AiReviewIteration> Iterations => _iterations;

    public static AiReview Create(
        Guid submissionId,
        Guid issueId,
        Guid userId,
        Guid authorId,
        VcsProvider provider,
        string repoFullName,
        int pullNumber,
        string pullRequestUrl)
    {
        return new AiReview(
            submissionId,
            issueId,
            userId,
            authorId,
            provider,
            repoFullName,
            pullNumber,
            pullRequestUrl,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     Атомарная попытка перейти QUEUED→RUNNING. Возвращает true если this
    ///     instance — owner текущей iteration (no concurrent runner). Используется
    ///     handler'ом для in-memory guard ПОСЛЕ того как DB-level UPDATE подтвердил
    ///     эксклюзивный access. См. <c>IAiReviewsRepository.TryAcquireRunningLockAsync</c>.
    /// </summary>
    public void MarkRunningAfterAtomicAcquire(DateTimeOffset acquiredAt)
    {
        Status = AiReviewStatus.RUNNING;
        UpdatedAt = acquiredAt;
    }

    public void ResetForRestart(DateTimeOffset restartedAt)
    {
        Status = AiReviewStatus.QUEUED;
        LatestIterationId = null;
        LatestVerdict = null;
        UpdatedAt = restartedAt;
    }

    public AiReviewIteration StartCancelledIteration(string reason, string modelUsed)
    {
        AiReviewIteration iteration = StartIteration(string.Empty);
        iteration.Fail(reason, modelUsed);
        return iteration;
    }

    /// <summary>
    ///     Регистрирует новую iteration при старте run-iteration handler'а.
    ///     <see cref="IterationNumber"/> — sequential начиная с 1.
    ///     Iteration добавляется в коллекцию (EF трекает Added через field-access);
    ///     Status становится <see cref="AiReviewStatus.RUNNING"/>.
    /// </summary>
    public AiReviewIteration StartIteration(string commitSha)
    {
        int nextNumber = IterationsCount + 1;
        AiReviewIteration iteration = AiReviewIteration.Create(Id, nextNumber, commitSha);
        _iterations.Add(iteration);

        Status = AiReviewStatus.RUNNING;
        IterationsCount = nextNumber;
        UpdatedAt = DateTimeOffset.UtcNow;
        return iteration;
    }

    /// <summary>
    ///     После успешного завершения iteration: review status → READY,
    ///     denormalize latest verdict + iteration id для быстрых reads.
    /// </summary>
    public void OnIterationCompleted(AiReviewIteration iteration)
    {
        Status = AiReviewStatus.READY;
        LatestIterationId = iteration.Id;
        LatestVerdict = iteration.Verdict;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     После failed iteration: review status → FAILED. Latest pointer всё
    ///     равно ставим — UI должен показать причину последней попытки.
    /// </summary>
    public void OnIterationFailed(AiReviewIteration iteration)
    {
        Status = AiReviewStatus.FAILED;
        LatestIterationId = iteration.Id;
        LatestVerdict = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
