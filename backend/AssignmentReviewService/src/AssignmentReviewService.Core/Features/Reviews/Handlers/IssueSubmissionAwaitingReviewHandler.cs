using System.Text.RegularExpressions;
using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using Core.Database;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace AssignmentReviewService.Core.Features.Reviews.Handlers;

/// <summary>
///     Wolverine consumer для <c>progress.events / issue_submission.awaiting_review</c>.
///     Phase 8 (#15) — создаёт <see cref="AiReview"/> aggregate под submission, если:
///     <list type="bullet">
///         <item>Payload — корректный GitHub PR URL (<c>https://github.com/owner/repo/pull/N</c>);</item>
///         <item>есть active <see cref="VcsInstallation"/> с matching owner.</item>
///     </list>
///     Идемпотентен: повторный event на тот же SubmissionId — no-op
///     (DB unique constraint на <c>uq_ai_reviews_submission</c>).
///
///     Если no installation → AiReview создаётся со <c>Status=FAILED</c> и сразу
///     добавляется failed pseudo-iteration с <c>FailureReason="review.no_installation"</c>
///     для observability и UI lock copy.
///
///     Публикует <see cref="AiReviewQueuedForSubmission"/>, чтобы ProgressService
///     гейтнул <c>ReadyForHumanReview=false</c> на submission.
/// </summary>
public sealed class IssueSubmissionAwaitingReviewHandler
{
    // Принимаем каноничный PR URL плюс любой хвост: trailing slash / query / fragment /
    // sub-path (`#pullrequestreview-…`, `?diff=split`, `/files`, `/changes/<sha>`, `/commits`).
    // owner/repo/num всё равно захватываются; pull_request_url ниже пересобирается в каноничную
    // форму, поэтому GitHub API-вызовы (через repo_full_name + pull_number) хвост не несут. (#668)
    private static readonly Regex GITHUB_PR_URL_REGEX = new(
        @"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/pull/(?<num>\d+)(?:[/?#].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IAiReviewsRepository _reviews;
    private readonly IVcsInstallationsRepository _installations;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly IAssignmentReviewAiModelSettingsResolver _settings;
    private readonly ILogger<IssueSubmissionAwaitingReviewHandler> _logger;

    public IssueSubmissionAwaitingReviewHandler(
        IAiReviewsRepository reviews,
        IVcsInstallationsRepository installations,
        IOutboxService outbox,
        ITransactionManager transactions,
        IAssignmentReviewAiModelSettingsResolver settings,
        ILogger<IssueSubmissionAwaitingReviewHandler> logger)
    {
        _reviews = reviews;
        _installations = installations;
        _outbox = outbox;
        _transactions = transactions;
        _settings = settings;
        _logger = logger;
    }

    public async Task HandleAsync(IssueSubmissionAwaitingReview message, CancellationToken ct)
    {
        if (!message.AiReviewRequested)
        {
            _logger.LogInformation(
                "AI review not requested for submission {SubmissionId} — skipped by project/issue settings.",
                message.SubmissionId);
            return;
        }

        // Платформенный тумблер (#355): если AI-проверка выключена админом — не создаём
        // AiReview и не публикуем gate-event. Submission идёт сразу на ручное ревью автором
        // (ProgressService держит ReadyForHumanReview=true, т.к. AiReviewQueuedForSubmission
        // не публикуется).
        if (!await _settings.ResolveReviewEnabledAsync(ct))
        {
            _logger.LogInformation(
                "AI review disabled — submission {SubmissionId} skipped (no AiReview created).",
                message.SubmissionId);
            return;
        }

        // Идемпотентность через unique constraint на submission_id.
        if (await _reviews.ExistsAsync(r => r.SubmissionId == message.SubmissionId, ct))
        {
            _logger.LogDebug(
                "AiReview already exists for submission {SubmissionId} — skip.",
                message.SubmissionId);
            return;
        }

        Match match = GITHUB_PR_URL_REGEX.Match(message.Payload ?? string.Empty);
        if (!match.Success)
        {
            _logger.LogInformation(
                "Submission {SubmissionId} payload не GitHub PR URL ({Payload}) — AiReview не создаётся.",
                message.SubmissionId,
                message.Payload);
            return;
        }

        string ownerLogin = match.Groups["owner"].Value;
        string ownerLoginLower = ownerLogin.ToLowerInvariant();
        string repoName = match.Groups["repo"].Value;
        string repoFullName = $"{ownerLogin}/{repoName}";
        int pullNumber = int.Parse(match.Groups["num"].Value, System.Globalization.CultureInfo.InvariantCulture);
        // Каноничная форма — отрезаем trailing slash/query/fragment/sub-path, который мог
        // вставить студент (например `#pullrequestreview-…`), чтобы UI-ссылка и GitHub-вызовы
        // не несли мусорный хвост. (#668)
        string pullRequestUrl = $"https://github.com/{ownerLogin}/{repoName}/pull/{pullNumber}";

        VcsInstallation? installation = await _installations.GetByAsync(
            i => i.Provider == VcsProvider.GITHUB
                 && i.OwnerLogin == ownerLoginLower
                 && i.Status == VcsInstallationStatus.ACTIVE,
            ct);

        AiReview review = AiReview.Create(
            submissionId: message.SubmissionId,
            issueId: message.IssueId,
            userId: message.StudentUserId,
            authorId: message.AuthorId,
            provider: VcsProvider.GITHUB,
            repoFullName: repoFullName,
            pullNumber: pullNumber,
            pullRequestUrl: pullRequestUrl);

        await _reviews.AddAsync(review, ct);

        if (installation is null)
        {
            // No installation — review создаётся в FAILED, pseudo-iteration
            // фиксирует причину, чтобы UI мог показать lock copy.
            AiReviewIteration failed = review.StartIteration(commitSha: string.Empty);
            failed.Fail("review.no_installation", modelUsed: string.Empty);
            review.OnIterationFailed(failed);

            _logger.LogWarning(
                "Submission {SubmissionId}: no active GitHub installation for {Owner} — AiReview marked FAILED.",
                message.SubmissionId,
                ownerLogin);
        }
        else
        {
            // Installation есть → publish gate event для ProgressService.
            await _outbox.PublishAsync(new AiReviewQueuedForSubmission(
                AiReviewId: review.Id,
                SubmissionId: message.SubmissionId,
                UserId: message.StudentUserId,
                IssueId: message.IssueId,
                QueuedAt: DateTimeOffset.UtcNow));

            // Auto-run первой итерации — студенту не нужно вручную жать «Запустить
            // AI-проверку». Локальное сообщение через тот же durable outbox:
            // доставляется ПОСЛЕ commit'а review-row'а, поэтому RunIteration видит
            // готовый review. Best-effort — failure итерации не ломает submission.
            // Та же команда используется manual-flow'ом (#357) с ModelOverride
            // от admin'а; auto-flow всегда передаёт null.
            await _outbox.PublishAsync(new RunAiReviewRequested(review.Id, ModelOverride: null));
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            // Идемпотентная гонка: если этот handler стартанул дважды (Wolverine
            // retry перед ack или duplicate event) и оба прошли `ExistsAsync`
            // до SaveChanges первого — unique constraint на submission_id
            // отбьёт второй INSERT. Это benign — review уже существует, повторим
            // ExistsAsync и спокойно выйдем (вместо DLQ).
            if (await _reviews.ExistsAsync(r => r.SubmissionId == message.SubmissionId, ct))
            {
                _logger.LogDebug(
                    "AiReview for submission {SubmissionId} created concurrently — idempotent no-op.",
                    message.SubmissionId);
                return;
            }

            // Non-idempotent failure (transient DB error etc.) — throw чтобы Wolverine
            // retry'нул через стандартный error policy. Просто log + swallow стоил бы
            // нам потери AiReview row'а: source IssueSubmissionAwaitingReview event
            // публикуется один раз и не пере-генерируется на следующих push'ах PR.
            _logger.LogWarning(
                "Failed to persist AiReview for submission {SubmissionId}: {Code} — Wolverine retry.",
                message.SubmissionId,
                save.Error.Messages[0].Code);
            throw save.Error.ToException();
        }
    }
}
