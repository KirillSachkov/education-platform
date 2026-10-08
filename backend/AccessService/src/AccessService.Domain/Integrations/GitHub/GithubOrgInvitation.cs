namespace AccessService.Domain.Integrations.GitHub;

/// <summary>
///     Запись об отправленном GitHub-приглашении пользователю в org плана.
///     Уникально per (PlanId, UserId). Жизненный цикл: PENDING → ACCEPTED|EXPIRED|CANCELED|FAILED.
///     ACCEPTED — конечное состояние (повторно invite не запрашиваем).
///     FAILED — локальная ошибка; UI показывает retry-кнопку.
/// </summary>
public sealed class GithubOrgInvitation
{
    private GithubOrgInvitation() { } // EF

    private GithubOrgInvitation(
        Guid id,
        Guid planId,
        Guid userId,
        string githubLogin,
        string orgLogin,
        long? githubInvitationId,
        GithubInvitationStatus status,
        string? failureReason,
        DateTimeOffset createdAt)
    {
        Id = id;
        PlanId = planId;
        UserId = userId;
        GithubLogin = githubLogin.ToLowerInvariant();
        OrgLogin = orgLogin.ToLowerInvariant();
        GithubInvitationId = githubInvitationId;
        Status = status;
        FailureReason = failureReason;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid PlanId { get; private set; }

    public Guid UserId { get; private set; }

    public string GithubLogin { get; private set; } = null!;

    public string OrgLogin { get; private set; } = null!;

    public long? GithubInvitationId { get; private set; }

    public GithubInvitationStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    public static GithubOrgInvitation CreatePending(
        Guid planId,
        Guid userId,
        string githubLogin,
        string orgLogin,
        long githubInvitationId,
        DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(),
            planId,
            userId,
            githubLogin,
            orgLogin,
            githubInvitationId,
            GithubInvitationStatus.PENDING,
            failureReason: null,
            now);

    public static GithubOrgInvitation CreateAlreadyMember(
        Guid planId,
        Guid userId,
        string githubLogin,
        string orgLogin,
        DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(),
            planId,
            userId,
            githubLogin,
            orgLogin,
            githubInvitationId: null,
            GithubInvitationStatus.ACCEPTED,
            failureReason: null,
            now)
        {
            AcceptedAt = now,
            LastSyncedAt = now,
        };

    public static GithubOrgInvitation CreateFailed(
        Guid planId,
        Guid userId,
        string githubLogin,
        string orgLogin,
        string failureReason,
        DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(),
            planId,
            userId,
            githubLogin,
            orgLogin,
            githubInvitationId: null,
            GithubInvitationStatus.FAILED,
            failureReason,
            now);

    public void MarkAccepted(DateTimeOffset now)
    {
        if (Status == GithubInvitationStatus.ACCEPTED) return;
        Status = GithubInvitationStatus.ACCEPTED;
        AcceptedAt = now;
        LastSyncedAt = now;
        FailureReason = null;
    }

    /// <summary>
    ///     Повторная попытка после терминально-неуспешного статуса (#501): новое
    ///     приглашение отправлено — строка возвращается в PENDING с новым GitHub
    ///     invitation id.
    /// </summary>
    public void MarkPendingAgain(long githubInvitationId, DateTimeOffset now)
    {
        Status = GithubInvitationStatus.PENDING;
        GithubInvitationId = githubInvitationId;
        FailureReason = null;
        AcceptedAt = null; // инвариант: PENDING-строка не несёт время принятия
        LastSyncedAt = now;
    }

    /// <summary>
    ///     Повторная попытка снова не удалась — фиксируем актуальную причину (#501).
    ///     Условия могли смениться (был no_installation, стал github_user_not_found).
    /// </summary>
    public void MarkFailedAgain(string failureReason, DateTimeOffset now)
    {
        Status = GithubInvitationStatus.FAILED;
        GithubInvitationId = null;
        FailureReason = failureReason;
        LastSyncedAt = now;
    }

    public void MarkExpired(DateTimeOffset now)
    {
        Status = GithubInvitationStatus.EXPIRED;
        LastSyncedAt = now;
    }

    public void MarkCanceled(DateTimeOffset now)
    {
        Status = GithubInvitationStatus.CANCELED;
        LastSyncedAt = now;
    }

    public void MarkSynced(DateTimeOffset now) => LastSyncedAt = now;
}
