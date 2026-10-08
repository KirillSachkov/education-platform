using System.Diagnostics;
using System.Globalization;
using System.Text;
using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Diagnostics;
using AssignmentReviewService.Core.Features.Reviews.Errors;
using AssignmentReviewService.Core.Features.Reviews.Models;
using AssignmentReviewService.Core.Features.Reviews.Services;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.AiSettings;
using AssignmentReviewService.Domain.Vcs;
using Core.Abstractions;
using Core.Database;
using FluentValidation;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace AssignmentReviewService.Core.Features.Reviews.UseCases;

public sealed record RunIterationResponse(
    Guid IterationId,
    int IterationNumber,
    string Status,
    string? Verdict,
    string? Summary,
    int InlineCommentsCount,
    long? GitHubReviewId,
    string ModelUsed);

/// <param name="SystemTriggered">
///     true — запуск инициирован системой (auto-run на submission), не HTTP-юзером.
///     Пропускает owner/admin authz: вызывающий handler уже не в request-scope, а
///     review гарантированно принадлежит студенту-сабмиттеру. Endpoint всегда шлёт false.
/// </param>
public sealed record RunIterationCommand(
    Guid AiReviewId,
    string? ModelOverride = null,
    bool SystemTriggered = false,
    bool AllowOversizedDiff = false,
    bool ForceFresh = false) : ICommand;

public sealed class RunIterationValidator : AbstractValidator<RunIterationCommand>
{
    public RunIterationValidator()
    {
        RuleFor(x => x.AiReviewId).NotEmpty();
        RuleFor(x => x.ModelOverride).MaximumLength(AiModelSlot.MAX_MODEL_LENGTH);
    }
}

// HTTP endpoint больше не висит на этом handler'е — см. RequestRunIteration.cs.
// После #357 manual flow идёт через durable outbox: endpoint публикует
// RunAiReviewRequested и сразу возвращает 200, Wolverine-handler гоняет всю
// логику ниже в отдельном scope'е. SystemTriggered=true гарантирует, что HTTP
// authz не выполняется здесь (она уже сделана в RequestRunIterationHandler).

/// <summary>
///     Ядро Phase 7: запускает iteration AI-проверки и публикует результат в PR.
///     Шаги:
///     <list type="number">
///         <item>Load review + ownership check</item>
///         <item>Domain rate-limit check (per-user/24h, per-submission caps)</item>
///         <item>Lookup VCS installation для repo owner'а</item>
///         <item>Fetch PR + diff из GitHub</item>
///         <item>Diff size guard (DIFF_TOO_LARGE)</item>
///         <item>AiReviewer: retrieve context → build prompt → LLM call → parse</item>
///         <item>Post review в PR (если есть inline comments или non-no-op verdict)</item>
///         <item>Persist iteration + denorm latest на review + outbox event</item>
///     </list>
/// </summary>
public sealed class RunIterationHandler : ICommandHandler<RunIterationResponse, RunIterationCommand>
{
    private readonly IAiReviewsRepository _reviews;
    private readonly IVcsInstallationsRepository _installations;
    private readonly IStudentPrMessagesRepository _messages;
    private readonly IVcsProvider _vcs;
    private readonly AiReviewer _reviewer;
    private readonly RepoContextBuilder _repoContextBuilder;
    private readonly RateLimitChecker _rateLimit;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _user;
    private readonly IValidator<RunIterationCommand> _validator;
    private readonly IAssignmentReviewAiModelSettingsResolver _settingsResolver;
    private readonly IOptions<AssignmentReviewAiOptions> _options;
    private readonly AssignmentReviewMetrics _metrics;
    private readonly ILogger<RunIterationHandler> _logger;

    public RunIterationHandler(
        IAiReviewsRepository reviews,
        IVcsInstallationsRepository installations,
        IStudentPrMessagesRepository messages,
        IVcsProvider vcs,
        AiReviewer reviewer,
        RepoContextBuilder repoContextBuilder,
        RateLimitChecker rateLimit,
        ITransactionManager transactions,
        IOutboxService outbox,
        UserScopedData user,
        IValidator<RunIterationCommand> validator,
        IAssignmentReviewAiModelSettingsResolver settingsResolver,
        IOptions<AssignmentReviewAiOptions> options,
        AssignmentReviewMetrics metrics,
        ILogger<RunIterationHandler> logger)
    {
        _reviews = reviews;
        _installations = installations;
        _messages = messages;
        _vcs = vcs;
        _reviewer = reviewer;
        _repoContextBuilder = repoContextBuilder;
        _rateLimit = rateLimit;
        _transactions = transactions;
        _outbox = outbox;
        _user = user;
        _validator = validator;
        _settingsResolver = settingsResolver;
        _options = options;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Result<RunIterationResponse, Error>> Handle(
        RunIterationCommand command, CancellationToken ct)
    {
        using Activity? activity = AssignmentReviewActivities.StartIteration(command.AiReviewId);
        Stopwatch stopwatch = Stopwatch.StartNew();
        // #713 — граница «до текущего запуска»: в prompt берём только реплики студента,
        // созданные ДО этого момента (не подхватываем комменты, прилетевшие во время прогона).
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Error.Validation("review.run_iteration.invalid", validation.Errors[0].ErrorMessage);

        // Платформенный тумблер (#355): AI-проверка выключена админом → отбиваем и
        // ручной re-run, и любой авто-ран. Авто-ран при выключенном тумблере вообще
        // не публикуется (гейт в IssueSubmissionAwaitingReviewHandler), но этот чек —
        // вторая линия защиты для re-run'ов на review'ах, созданных пока был включён.
        if (!await _settingsResolver.ResolveReviewEnabledAsync(ct))
            return ReviewErrors.ReviewDisabled();

        AiReview? review = await _reviews.GetByAsync(r => r.Id == command.AiReviewId, ct);
        if (review is null)
            return ReviewErrors.ReviewNotFound(command.AiReviewId);

        // Manual re-check — только автор курса или админ. Студент (review.UserId)
        // больше НЕ может дёргать проверку: первый прогон идёт авто-runom на сабмите
        // (SystemTriggered=true минует authz), а перепроверку инициирует ревьюер.
        if (!command.SystemTriggered && !_user.IsOwnerOrAdmin(review.AuthorId))
            return ReviewErrors.AccessDenied();

        // In-memory гард — отсечь явный RUNNING до cheap-операций. Не гарантирует
        // exclusivity при concurrent requests — это делает TryAcquireRunningLockAsync
        // ниже, прямо перед LLM call'ом.
        if (review.Status == AiReviewStatus.RUNNING)
            return ReviewErrors.ReviewAlreadyRunning(review.Id);

        UnitResult<Error> rateLimitCheck = await _rateLimit.EnsureCanRunAsync(
            review.UserId, review.AuthorId, review.SubmissionId, ct);
        if (rateLimitCheck.IsFailure)
            return rateLimitCheck.Error;

        // VCS installation lookup. Owner login извлекаем из repo full_name
        // (формат "owner/repo" — gateway формирует при создании review).
        string ownerLogin = review.RepoFullName.Split('/', 2)[0].ToLowerInvariant();
        VcsInstallation? installation = await _installations.GetByAsync(
            i => i.Provider == VcsProvider.GITHUB
                 && i.OwnerLogin == ownerLogin
                 && i.Status == VcsInstallationStatus.ACTIVE,
            ct);

        if (installation is null)
            return ReviewErrors.NoInstallation(review.RepoFullName);

        Result<VcsPullRequest, Error> prResult = await _vcs.GetPullRequestAsync(
            installation.InstallationId, review.RepoFullName, review.PullNumber, ct);
        if (prResult.IsFailure)
            return MapVcsError(prResult.Error, review.RepoFullName);

        if (prResult.Value.IsDraft)
            return ReviewErrors.DraftNotSupported(review.RepoFullName, review.PullNumber);

        // Idempotency: повторный run на тот же commit_sha вернёт результат
        // предыдущей completed-итерации, не дёргая GitHub diff API и LLM.
        // Поможет при двойных кликах на «Запустить AI» и при retry на сетевых
        // ошибках. FAILED iteration'ы не блокируют retry — пользователь явно
        // хочет переcanRun проверку.
        AiReviewIteration? completedForSha = command.ForceFresh
            ? null
            : review.Iterations
                .Where(i => i.Status == AiReviewIterationStatus.COMPLETED
                            && string.Equals(i.CommitSha, prResult.Value.HeadSha, StringComparison.Ordinal))
                .OrderByDescending(i => i.IterationNumber)
                .FirstOrDefault();
        if (completedForSha is not null)
        {
            // Recovery path (#474): review может быть в QUEUED (startup stale-RUNNING reset)
            // или LatestIterationId=Guid.Empty (pre-#474 bug). Repair'им denorm + republish
            // verdict event чтобы ProgressService gate оставался консистентным.
            if (review.Status != AiReviewStatus.READY
                || review.LatestIterationId != completedForSha.Id)
            {
                review.OnIterationCompleted(completedForSha); // completedForSha.Id — реальный GUID из DB
                await _outbox.PublishAsync(new AiReviewIterationCompleted(
                    review.Id,
                    completedForSha.Id,
                    review.SubmissionId,
                    review.UserId,
                    review.IssueId,
                    completedForSha.IterationNumber,
                    completedForSha.Verdict?.ToString() ?? string.Empty,
                    completedForSha.GitHubReviewId,
                    completedForSha.CompletedAt!.Value));
                UnitResult<Error> repairSave = await _transactions.SaveChangesAsync(ct);
                if (repairSave.IsFailure)
                    _logger.LogWarning(
                        "Idempotent replay: failed to repair stale review {ReviewId} " +
                        "(LatestIterationId was {OldId}, expected {NewId}). " +
                        "ProgressService denorm may be stale until next retry.",
                        review.Id, review.LatestIterationId, completedForSha.Id);
            }

            _logger.LogInformation(
                "Idempotent run-iteration replay for review {ReviewId} commit {CommitSha} → iteration {IterationId}",
                review.Id, prResult.Value.HeadSha, completedForSha.Id);
            return new RunIterationResponse(
                completedForSha.Id,
                completedForSha.IterationNumber,
                completedForSha.Status.ToString(),
                completedForSha.Verdict?.ToString(),
                completedForSha.Summary,
                completedForSha.InlineCommentsCount,
                completedForSha.GitHubReviewId,
                completedForSha.ModelUsed);
        }

        // Phase 11 (#15) — model override гейтится `PlatformPermissions.Ai.OVERRIDE_MODEL`
        // (#281: вынесли из inline IsAdmin в named permission). Резолвим заранее,
        // чтобы failed iteration носила правильное имя модели.
        string? effectiveOverride = !string.IsNullOrWhiteSpace(command.ModelOverride)
            && _user.HasPermission(PlatformPermissions.Ai.OVERRIDE_MODEL)
                ? command.ModelOverride!.Trim()
                : null;

        // Re-review baseline (#17 + #334 + #581): различаем ДВА prior'а.
        //  • priorWithinReview — последняя COMPLETED iteration ЭТОГО review'а
        //    (ручной «Перепроверить» по той же submission). Только она годится для
        //    инкрементального диффа: её iteration #1 уже видел весь PR, поэтому
        //    incremental compare поверх неё не теряет контекст решения.
        //  • priorForPr — последняя COMPLETED iteration того же студента по тому же PR
        //    (включая ПРОШЛЫЕ review'ы / ре-сабмиты). Нужна для anti-spam carry-forward
        //    и для prior-review context, НО НЕ для инкрементального диффа.
        //
        // Почему ре-сабмит (cross-review) идёт ПОЛНЫМ diff'ом, а не incremental:
        // каждый ре-сабмит создаёт НОВЫЙ AiReview без своих iteration'ов. Если брать
        // baseline из прошлого review'а и слать только `compare prevSha…headSha` (дифф
        // одного нового коммита), модель видит лишь последний коммит и ложно объявляет
        // фичу «не реализованной» — ядро решения лежит в коммитах прошлых сабмитов, вне
        // диффа (прод-кейс naturalnayasmetanka/ds#18: 6 ложных MAJOR подряд, #581).
        // Поэтому ре-сабмит ревьюим ВЕСЬ PR; вердикт по полному решению. Prior-review
        // context при этом сохраняем — модель видит прошлый фидбэк, но верифицирует его
        // на полном diff'е, а не повторяет вслепую.
        AiReviewIteration? priorWithinReview = null;
        AiReviewIteration? priorForPr = null;
        if (!command.ForceFresh)
        {
            priorWithinReview = review.Iterations
                .Where(i => i.Status == AiReviewIterationStatus.COMPLETED
                            && !string.IsNullOrEmpty(i.CommitSha))
                .OrderByDescending(i => i.IterationNumber)
                .FirstOrDefault();

            priorForPr = priorWithinReview
                ?? await _reviews.GetLatestCompletedIterationForPullRequestAsync(
                    review.Provider, review.RepoFullName, review.PullNumber, review.UserId, review.Id, ct);
        }

        // Anti-spam (#334): ре-сабмит без новых коммитов (head == уже проверенный sha)
        // — НЕ дёргаем LLM. Переносим прошлый вердикт на новую submission
        // (carry-forward iteration) + публикуем event, чтобы gate ре-применил тот же
        // вердикт. within-review дубль того же sha отсечён idempotency-replay'ем выше,
        // так что сюда попадает только cross-review prior с валидным verdict'ом.
        if (priorForPr is { Verdict: not null }
            && string.Equals(priorForPr.CommitSha, prResult.Value.HeadSha, StringComparison.Ordinal))
        {
            return await PersistCarryForwardIterationAsync(
                review, priorForPr, prResult.Value.HeadSha, ct);
        }

        // Инкрементальный compare — ТОЛЬКО within-review. Cross-review (ре-сабмит) и
        // первое ревью → полный PR diff.
        bool isIncremental = priorWithinReview is not null;

        Result<VcsDiff, Error> diffResult = isIncremental
            ? await _vcs.CompareAsync(
                installation.InstallationId, review.RepoFullName,
                priorWithinReview!.CommitSha, prResult.Value.HeadSha, ct)
            : await _vcs.GetPullRequestDiffAsync(
                installation.InstallationId, review.RepoFullName, review.PullNumber, ct);
        if (diffResult.IsFailure)
            return MapVcsError(diffResult.Error, review.RepoFullName);

        VcsDiff diff = diffResult.Value;
        _metrics.RecordDiffAdditions(diff.TotalAdditions);

        // Инкрементальный compare (within-review) дал пустой diff: head отличается, но
        // файловых изменений нет (rebase / пустой merge-commit). LLM звать незачем —
        // отдаём результат прошлой итерации этого же review'а (gate уже применён).
        if (isIncremental && diff.Files.Count == 0)
        {
            _logger.LogInformation(
                "Incremental re-review for {ReviewId}: no new changes since {PrevSha}; carrying forward iteration {IterationId}.",
                review.Id, priorWithinReview!.CommitSha, priorWithinReview.Id);
            return new RunIterationResponse(
                priorWithinReview.Id,
                priorWithinReview.IterationNumber,
                priorWithinReview.Status.ToString(),
                priorWithinReview.Verdict?.ToString(),
                priorWithinReview.Summary,
                priorWithinReview.InlineCommentsCount,
                priorWithinReview.GitHubReviewId,
                priorWithinReview.ModelUsed);
        }

        // Prior-review context (#17 + #383) — короткий TRUSTED блок для prompt'а, чтобы
        // модель ревьюила изменения в свете прошлого фидбэка. Берём ЛЮБОЙ prior по PR
        // (within-review ИЛИ ре-сабмит): на полном diff'е ре-сабмита это даёт модели
        // «что я просила в прошлый раз» без потери контекста. Помимо verdict + summary
        // тянем ТЕЛА inline-комментариев прошлой итерации из GitHub (по её
        // GitHubReviewId): так модель видит, что именно она просила исправить, и не
        // поднимает уже закрытые замечания заново.
        string? previousReviewContext = priorForPr is not null
            ? await BuildPreviousReviewContextAsync(review, installation, priorForPr, ct)
            : null;

        // Student replies context (#713): реплики/вопросы студента к прошлым замечаниям,
        // созданные ДО этого запуска. Даём модели как контекст (не команды) — чтобы она не
        // повторяла уже объяснённые/учтённые замечания и учитывала вопросы студента.
        string? studentReplies = await BuildStudentRepliesContextAsync(review.Id, runStartedAt, ct);

        // Repo-context (#798): карта репозитория + bounded-цикл дозапроса файлов.
        // Флаг: DB-singleton → config → false. Сборка best-effort — сбой GitHub на
        // дереве НЕ роняет итерацию (builder сам деградирует до частичного контекста).
        RepoReviewContext? repoContext = null;
        if (await _settingsResolver.ResolveRepoContextEnabledAsync(ct))
        {
            repoContext = await _repoContextBuilder.BuildAsync(
                installation.InstallationId, review.RepoFullName, prResult.Value.HeadSha, ct);
        }

        // #2: на ВРЕМЯ прогона переводим submission в «В проверке»
        // (ready_for_human_review=false), чтобы автор видел перемещение карточки при
        // ручном «Перепроверить». Публикуем тот же gate-event, что и auto-flow.
        // Flush'им сразу — review-сущность здесь ещё НЕ модифицирована (lock берём ниже),
        // поэтому SaveChanges сливает только outbox-сообщение, без concurrency-риска на
        // review-row'е. По завершении AiReviewIterationCompleted раз-гейтит
        // (ApplyVerdictGate → Approve/RequestChanges/Finalize). ProgressService-handler
        // гейтит только PENDING-сабмишены, так что взятую автором (IN_REVIEW) работу
        // это не трогает. Idempotent-replay / carry-forward возвращаются выше по коду —
        // сюда попадает только реальный LLM-прогон.
        await _outbox.PublishAsync(new AiReviewQueuedForSubmission(
            AiReviewId: review.Id,
            SubmissionId: review.SubmissionId,
            UserId: review.UserId,
            IssueId: review.IssueId,
            QueuedAt: DateTimeOffset.UtcNow));
        UnitResult<Error> regateSave = await _transactions.SaveChangesAsync(ct);
        if (regateSave.IsFailure)
            return regateSave.Error;

        // Атомарный gate против concurrent run-iteration. Conditional UPDATE
        // на DB-уровне гарантирует exactly-один caller проходит дальше при
        // двух одновременных POST'ах. До этого момента — cheap reads / lookups,
        // которые можно делать параллельно без проблем; LLM call ниже — expensive
        // ($), и его нельзя дублировать.
        DateTimeOffset? leaseAcquiredAt = await _reviews.TryAcquireRunningLockAsync(review.Id, ct);
        if (leaseAcquiredAt is null)
            return ReviewErrors.ReviewAlreadyRunning(review.Id);

        // EF tracker нужно синхронизировать чтобы последующий save был consistent.
        review.MarkRunningAfterAtomicAcquire(leaseAcquiredAt.Value);

        // AuthorId зафиксирован при создании AiReview в IssueSubmissionAwaitingReviewHandler
        // из payload'а IssueSubmissionAwaitingReview.AuthorId. Хранится для денорм
        // в integration event'ах и audit; pipeline reviewer'у не передаётся (#320).
        // #405: AI-провайдер может временно лечь / отдать непредвиденную ошибку. Ретраим
        // LLM-вызов до MaxLlmAttempts раз с нарастающей паузой, прежде чем зафиксировать
        // FAILED и отдать студенту «AI недоступна». Карточка всё это время остаётся
        // «В проверке» (lock держится, ready_for_human_review=false). Ретраим только
        // транзиентные infra-ошибки провайдера (review.llm.unavailable — недоступен/таймаут/
        // пустой ответ/непредвиденное исключение); детерминированные доменные ошибки (diff
        // too large, draft, no installation, GitHub отверг payload) ретраить бессмысленно.
        Result<ParsedAiReview, Error> aiResult = await RunReviewerWithRetryAsync(
            review, prResult.Value, diff, effectiveOverride, previousReviewContext,
            studentReplies, repoContext, command.AllowOversizedDiff, ct);

        if (aiResult.IsFailure)
        {
            if (!await _reviews.IsRunningLeaseCurrentAsync(review.Id, leaseAcquiredAt.Value, CancellationToken.None))
                return ReviewErrors.ReviewSuperseded(review.Id);

            // ct may already be canceled — persist the recovery iteration on a fresh
            // token so the lock-releasing write still completes.
            CancellationToken persistCt = ct.IsCancellationRequested ? CancellationToken.None : ct;
            return await PersistFailedIterationAsync(
                review, diff.HeadSha, aiResult.Error, effectiveOverride,
                command.AllowOversizedDiff, persistCt);
        }

        ParsedAiReview parsed = aiResult.Value;

        long? gitHubReviewId = null;
        int postedInlineCount = 0;
        if (parsed.InlineComments.Count > 0 || parsed.Verdict != AiReviewVerdict.LOOKS_GOOD)
        {
            if (!await _reviews.IsRunningLeaseCurrentAsync(review.Id, leaseAcquiredAt.Value, ct))
                return ReviewErrors.ReviewSuperseded(review.Id);

            // Sanitize prompt-injection: AI может вернуть текст с GitHub @mentions,
            // что триггерит notifications настоящим юзерам. Заменяем @ на
            // fullwidth ＠ (U+FF20) — визуально неотличимо, GitHub mention-parser
            // игнорирует. Эскейпим И summary, И inline-comment bodies + suggestions.
            string safeSummary = EscapeMentions(parsed.Summary);
            // Человеко-понятный заголовок вместо сырого вердикта (#383): LOOKS_GOOD и
            // MINOR_ISSUES платформа авто-апрувит, поэтому для них пишем «принято»
            // (для MINOR — с пометкой, что замечания необязательны), чтобы студента не
            // путал «verdict: MINOR_ISSUES» при фактическом одобрении. MAJOR/OFF_TOPIC —
            // явный сигнал «нужны правки».
            string verdictHeadline = parsed.Verdict switch
            {
                AiReviewVerdict.LOOKS_GOOD => "✅ Задание принято — замечаний нет.",
                AiReviewVerdict.MINOR_ISSUES =>
                    "✅ Задание принято. Ниже — необязательные замечания: посмотри по желанию, переотправлять не нужно.",
                AiReviewVerdict.MAJOR_ISSUES =>
                    "🔧 Нужны правки: внеси изменения по замечаниям ниже и переотправь задание.",
                AiReviewVerdict.OFF_TOPIC =>
                    "⚠️ PR пока не соответствует заданию — проверь, что отправляешь нужную работу.",
                _ => string.Empty,
            };
            string body = $"## AI-ревью (итерация {review.IterationsCount + 1})\n\n" +
                          $"{verdictHeadline}\n\n" +
                          safeSummary;

            // #368: GitHub отвергает ВЕСЬ review (422) если хоть один inline-коммент
            // указывает на строку вне diff'а. Отбрасываем такие позиции до поста, чтобы
            // шальной комментарий LLM не утопил всё ревью. Файлы без распарсенных
            // hunk'ов валидировать нечем — пропускаем их и полагаемся на summary-only
            // fallback ниже.
            List<VcsReviewComment> safeComments = parsed.InlineComments
                .Where(c => IsCommentablePosition(c.Path, c.Line, diff))
                .Select(c => new VcsReviewComment(
                    c.Path,
                    c.Line,
                    EscapeMentions(c.Body),
                    c.Suggestion is null ? null : EscapeMentions(c.Suggestion)))
                .ToList();
            postedInlineCount = safeComments.Count;

            VcsReviewRequest reviewRequest = new(diff.HeadSha, body, safeComments);
            Result<VcsPostedReview, Error> postResult = await _vcs.PostReviewAsync(
                installation.InstallationId, review.RepoFullName, review.PullNumber, reviewRequest, ct);

            // #368: 422 (vcs.invalid_request) — GitHub отверг наш payload (позиция вне
            // diff'а, устаревший commit_id и т.п.). Повтор того же payload'а бесполезен,
            // но summary без inline-комментов почти всегда проходит — постим его, чтобы
            // итерация завершилась, а не падала. 5xx/network (vcs.unavailable) НЕ
            // ретраим — это реальная недоступность, caller покажет «попробуй позже».
            if (postResult.IsFailure
                && string.Equals(postResult.Error.Messages[0].Code, "vcs.invalid_request", StringComparison.Ordinal)
                && safeComments.Count > 0)
            {
                _logger.LogWarning(
                    "GitHub rejected review {ReviewId} with {Count} inline comment(s); retrying summary-only. Detail: {Detail}",
                    review.Id, safeComments.Count, postResult.Error.Messages[0].Message);
                VcsReviewRequest summaryOnly = new(diff.HeadSha, body, []);
                postResult = await _vcs.PostReviewAsync(
                    installation.InstallationId, review.RepoFullName, review.PullNumber, summaryOnly, ct);
                if (postResult.IsSuccess) postedInlineCount = 0;
            }

            if (postResult.IsFailure)
            {
                if (!await _reviews.IsRunningLeaseCurrentAsync(review.Id, leaseAcquiredAt.Value, CancellationToken.None))
                    return ReviewErrors.ReviewSuperseded(review.Id);

                // Posting failed — iteration считается failed (мы не показали
                // ничего пользователю в PR). Логируем, persist FAIL.
                _logger.LogWarning(
                    "PostReviewAsync failed for review {ReviewId}: {Code}",
                    review.Id, postResult.Error.Messages[0].Code);
                return await PersistFailedIterationAsync(
                    review,
                    diff.HeadSha,
                    MapVcsError(postResult.Error, review.RepoFullName),
                    effectiveOverride,
                    command.AllowOversizedDiff,
                    ct);
            }

            gitHubReviewId = postResult.Value.GitHubReviewId;
            _metrics.IncrementGitHubReviewPosted();
        }

        if (!await _reviews.IsRunningLeaseCurrentAsync(review.Id, leaseAcquiredAt.Value, ct))
            return ReviewErrors.ReviewSuperseded(review.Id);

        // Persist completed iteration. Save #1 позволяет EF ValueGenerator присвоить реальный
        // iteration.Id (AiReviewIteration использует nav-collection pattern с Id=Guid.Empty).
        // OnIterationCompleted вызывается ПОСЛЕ save #1, чтобы LatestIterationId захватил
        // реальный GUID, а не Guid.Empty (#474).
        AiReviewIteration iteration = review.StartIteration(diff.HeadSha);
        iteration.Complete(
            parsed.Verdict,
            parsed.Summary,
            postedInlineCount,
            gitHubReviewId,
            parsed.ModelUsed,
            parsed.InputTokens,
            parsed.OutputTokens,
            requestedFiles: parsed.RequestedFiles,
            contextRounds: parsed.ContextRounds);

        UnitResult<Error> iterationSave = await _transactions.SaveChangesAsync(ct);
        if (iterationSave.IsFailure)
            return iterationSave.Error;

        // Save #1 done: iteration.Id теперь реальный GUID (ValueGenerator отработал).
        review.OnIterationCompleted(iteration);

        await _outbox.PublishAsync(new AiReviewIterationCompleted(
            review.Id,
            iteration.Id,
            review.SubmissionId,
            review.UserId,
            review.IssueId,
            iteration.IterationNumber,
            iteration.Verdict?.ToString() ?? string.Empty,
            iteration.GitHubReviewId,
            iteration.CompletedAt!.Value));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _metrics.IncrementIterationOutcome(parsed.Verdict, failureCode: null);
        _metrics.RecordIterationDuration(stopwatch.Elapsed, parsed.Verdict.ToString());
        _metrics.RecordRepoContextLoop(parsed.ContextRounds, parsed.RequestedFiles?.Count ?? 0);

        return new RunIterationResponse(
            iteration.Id,
            iteration.IterationNumber,
            iteration.Status.ToString(),
            iteration.Verdict?.ToString(),
            iteration.Summary,
            iteration.InlineCommentsCount,
            iteration.GitHubReviewId,
            iteration.ModelUsed);
    }

    /// <summary>
    ///     Прогоняет <see cref="AiReviewer.ReviewAsync"/> с ретраями на транзиентных
    ///     infra-сбоях AI-провайдера (#405). До <c>Limits.LlmMaxAttempts</c> попыток с
    ///     линейным backoff'ом (пауза перед попыткой N = N × <c>Limits.LlmRetryDelaySeconds</c>).
    ///     Исключение из LLM-вызова не выпускаем наружу — running-lock уже взят, escape
    ///     оставил бы review в RUNNING; конвертим в failure Result и решаем, ретраить или
    ///     вернуть для FAILED-фиксации.
    /// </summary>
    private async Task<Result<ParsedAiReview, Error>> RunReviewerWithRetryAsync(
        AiReview review,
        VcsPullRequest pullRequest,
        VcsDiff diff,
        string? modelOverride,
        string? previousReviewContext,
        string? studentReplies,
        RepoReviewContext? repoContext,
        bool allowOversizedDiff,
        CancellationToken ct)
    {
        AssignmentReviewLimits limits = _options.Value.Limits;
        int maxAttempts = Math.Max(1, limits.LlmMaxAttempts);
        int retryDelaySeconds = Math.Max(0, limits.LlmRetryDelaySeconds);

        Result<ParsedAiReview, Error> result = ReviewErrors.LlmUnavailable("not attempted");

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                result = await _reviewer.ReviewAsync(
                    review.IssueId, pullRequest, diff, modelOverride, previousReviewContext,
                    allowOversizedDiff,
                    // #690 — bump heartbeat_at после каждого batch'а / repo-context раунда,
                    // чтобы stale-watchdog не переотправил легитимно-долгое ревью.
                    onBatchProgress: heartbeatCt => _reviews.HeartbeatRunningAsync(review.Id, heartbeatCt),
                    studentReplies: studentReplies,
                    repoContext: repoContext,
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "AI review call threw (attempt {Attempt}/{Max}) for review {ReviewId}",
                    attempt, maxAttempts, review.Id);
                result = ReviewErrors.LlmUnavailable(
                    ex is OperationCanceledException ? "AI-проверка прервана (таймаут)." : ex.Message);
            }

            // Успех / детерминированная ошибка / последняя попытка / отмена ct — выходим
            // (caller зафиксирует FAILED, если это всё ещё failure).
            if (result.IsSuccess
                || !IsRetryableProviderError(result.Error)
                || attempt == maxAttempts
                || ct.IsCancellationRequested)
            {
                return result;
            }

            TimeSpan delay = TimeSpan.FromSeconds((long)retryDelaySeconds * attempt);
            _logger.LogWarning(
                "AI provider failure ({Code}) on attempt {Attempt}/{Max} for review {ReviewId}; retrying in {DelaySeconds}s.",
                result.Error.Messages[0].Code, attempt, maxAttempts, review.Id, delay.TotalSeconds);

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown / таймаут во время паузы — прекращаем ретраить, отдаём текущий
                    // failure для FAILED-фиксации (running-lock освободится).
                    return result;
                }
            }
        }

        return result;
    }

    // Транзиентные infra-сбои провайдера, на которых retry осмыслен (#405). Детерминированные
    // доменные ошибки (diff.too_large, no_installation, pr.draft_not_supported,
    // github.invalid_request) retry'ем не лечатся — их сюда не включаем.
    private static bool IsRetryableProviderError(Error error) =>
        string.Equals(error.Messages[0].Code, "review.llm.unavailable", StringComparison.Ordinal);

    private async Task<Result<RunIterationResponse, Error>> PersistFailedIterationAsync(
        AiReview review,
        string commitSha,
        Error error,
        string? modelOverride,
        bool allowOversizedDiff,
        CancellationToken ct)
    {
        AiReviewIteration iteration = review.StartIteration(commitSha);
        string code = error.Messages[0].Code;
        // Phase 11 (#15) — model name на failed iteration отражает effective
        // resolver result (override → DB → config), не только config-default.
        EffectiveSlot reviewerSlot = await _settingsResolver.ResolveReviewerAsync(modelOverride, ct);
        iteration.Fail(code, reviewerSlot.Model);
        _metrics.IncrementIterationOutcome(verdict: null, failureCode: code);

        // Save #1: EF ValueGenerator присваивает реальный iteration.Id (#474 — LatestIterationId fix).
        UnitResult<Error> iterationSave = await _transactions.SaveChangesAsync(ct);
        if (iterationSave.IsFailure)
            return iterationSave.Error;

        // Save #1 done: iteration.Id теперь реальный GUID.
        review.OnIterationFailed(iteration);

        await _outbox.PublishAsync(new AiReviewIterationCompleted(
            review.Id,
            iteration.Id,
            review.SubmissionId,
            review.UserId,
            review.IssueId,
            iteration.IterationNumber,
            string.Empty,
            null,
            iteration.CompletedAt!.Value));

        // #546: авто-ран пропущен из-за слишком большого diff'а — сигналим автору курса
        // (NotificationService шлёт «Большой PR — запустите проверку вручную»). Manual-run
        // с AllowOversizedDiff cap уже снял — уведомлять не о чем. Дедуп: только первый
        // too_large-фейл этого review (retry того же сабмита не флудит автору).
        bool isOversized = string.Equals(code, "review.diff.too_large", StringComparison.Ordinal);
        bool priorOversizedExists = review.Iterations.Any(i =>
            i.Id != iteration.Id
            && string.Equals(i.FailureReason, "review.diff.too_large", StringComparison.Ordinal));
        if (isOversized && !allowOversizedDiff && !priorOversizedExists)
        {
            await _outbox.PublishAsync(new AiReviewOversizedSkipped(
                review.Id,
                review.SubmissionId,
                review.UserId,
                review.AuthorId,
                review.IssueId,
                review.RepoFullName,
                review.PullNumber,
                iteration.CompletedAt!.Value));
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        // Возвращаем именно исходную domain-error caller'у — фронт показывает
        // соответствующий lock-copy. Iteration в БД уже зафиксирован.
        _logger.LogInformation(
            "Iteration {IterationId} for review {ReviewId} persisted as FAILED ({Code})",
            iteration.Id,
            review.Id,
            code);
        return error;
    }

    /// <summary>
    ///     Carry-forward (#334): ре-сабмит без новых коммитов. Переносим вердикт
    ///     прошлой completed-итерации на текущий review без LLM call'а и публикуем
    ///     <see cref="AiReviewIterationCompleted"/>, чтобы ProgressService gate
    ///     ре-применил тот же вердикт к новой submission (студент видит «изменений
    ///     нет, всё ещё MINOR» вместо тишины). Inline-комменты уже висят в PR от
    ///     прошлого ревью — новых не постим (count=0, githubReviewId=null), денег не тратим.
    /// </summary>
    private async Task<Result<RunIterationResponse, Error>> PersistCarryForwardIterationAsync(
        AiReview review, AiReviewIteration prior, string headSha, CancellationToken ct)
    {
        AiReviewIteration iteration = review.StartIteration(headSha);
        iteration.Complete(
            prior.Verdict!.Value,
            prior.Summary,
            inlineCommentsCount: 0,
            gitHubReviewId: null,
            prior.ModelUsed,
            inputTokens: null,
            outputTokens: null);

        // Save #1: EF ValueGenerator присваивает реальный iteration.Id (#474 — LatestIterationId fix).
        UnitResult<Error> iterationSave = await _transactions.SaveChangesAsync(ct);
        if (iterationSave.IsFailure)
            return iterationSave.Error;

        // Save #1 done: iteration.Id теперь реальный GUID.
        review.OnIterationCompleted(iteration);

        await _outbox.PublishAsync(new AiReviewIterationCompleted(
            review.Id,
            iteration.Id,
            review.SubmissionId,
            review.UserId,
            review.IssueId,
            iteration.IterationNumber,
            iteration.Verdict?.ToString() ?? string.Empty,
            iteration.GitHubReviewId,
            iteration.CompletedAt!.Value));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _metrics.IncrementIterationOutcome(prior.Verdict, failureCode: null);

        _logger.LogInformation(
            "Carry-forward iteration {IterationId} for review {ReviewId} (verdict {Verdict}) — no new commits since prior review.",
            iteration.Id, review.Id, prior.Verdict);

        return new RunIterationResponse(
            iteration.Id,
            iteration.IterationNumber,
            iteration.Status.ToString(),
            iteration.Verdict?.ToString(),
            iteration.Summary,
            iteration.InlineCommentsCount,
            iteration.GitHubReviewId,
            iteration.ModelUsed);
    }

    /// <summary>
    ///     Sanitize AI-generated text перед POST'ом в GitHub PR. Защищает от
    ///     случайных побочных эффектов на сторонние ресурсы:
    ///     <list type="bullet">
    ///         <item>@mentions — заменяем ASCII @ на fullwidth ＠ (U+FF20),
    ///         GitHub mention-parser не триггерит уведомления.</item>
    ///         <item>Issue-references вида #NNN — заменяем # на fullwidth ＃ (U+FF03),
    ///         чтобы AI summary не залинковал случайные PR'ы / issues в чужих репах.</item>
    ///         <item>Close-keywords (Closes / Fixes / Resolves #NNN) — обезвреживаются
    ///         тем же '#' replace'ом выше.</item>
    ///     </list>
    ///     Применяется к summary body, inline-comment bodies и suggestion blocks.
    /// </summary>
    private static string EscapeMentions(string text) =>
        text
            .Replace("@", "＠", StringComparison.Ordinal)
            .Replace("#", "＃", StringComparison.Ordinal);

    // На сколько обрезаем суммарный блок тел прошлых inline-комментов в prompt'е
    // (#383). Один комментарий обычно ≤200 символов; 4000 покрывает ~весь набор
    // итерации (cap MaxInlineComments), но не раздувает prompt при аномалии.
    private const int MaxPriorCommentsBlockChars = 4000;

    /// <summary>
    ///     Краткий контекст предыдущей completed-итерации (#17 + #383) для incremental
    ///     re-review prompt'а: verdict + summary + ТЕЛА inline-комментариев прошлого
    ///     review'а. Это наш собственный прошлый вывод (TRUSTED), даём модели чтобы она
    ///     оценивала новые изменения в свете того, что просила исправить раньше, и не
    ///     re-flag'ала уже закрытые замечания.
    ///     Inline-комменты тянутся из GitHub по <see cref="AiReviewIteration.GitHubReviewId"/>;
    ///     fetch best-effort — при ошибке/отсутствии review'а деградируем до verdict + summary.
    /// </summary>
    private async Task<string> BuildPreviousReviewContextAsync(
        AiReview review, VcsInstallation installation, AiReviewIteration prior, CancellationToken ct)
    {
        string verdict = prior.Verdict?.ToString() ?? "UNKNOWN";
        StringBuilder sb = new();
        sb.Append("Iteration ").Append(prior.IterationNumber).Append(" verdict: ").Append(verdict)
            .Append("\nSummary: ").Append(prior.Summary);

        if (prior.GitHubReviewId is not { } reviewId)
            return sb.ToString();

        Result<IReadOnlyList<VcsReviewComment>, Error> commentsResult = await _vcs.GetReviewCommentsAsync(
            installation.InstallationId, review.RepoFullName, review.PullNumber, reviewId, ct);

        if (commentsResult.IsFailure)
        {
            // Не валим итерацию из-за этого — prior-context опционален. Логируем и
            // продолжаем с verdict + summary.
            _logger.LogWarning(
                "Could not fetch prior review {GitHubReviewId} comments for review {ReviewId}: {Code}. "
                    + "Falling back to verdict+summary only.",
                reviewId, review.Id, commentsResult.Error.Messages[0].Code);
            return sb.ToString();
        }

        string commentsBlock = FormatPriorComments(commentsResult.Value);
        if (commentsBlock.Length > 0)
        {
            sb.Append("\nYour previous inline comments (what you asked the student to fix — verify these are now resolved, do NOT re-raise them if they are):\n")
                .Append(commentsBlock);
        }

        return sb.ToString();
    }

    private static string FormatPriorComments(IReadOnlyList<VcsReviewComment> comments)
    {
        StringBuilder sb = new();
        foreach (VcsReviewComment c in comments)
        {
            string body = c.Body.Trim();
            if (body.Length == 0) continue;

            string line = string.IsNullOrEmpty(c.Path)
                ? $"- {body}"
                : $"- {c.Path}:{c.Line.ToString(CultureInfo.InvariantCulture)} — {body}";

            // Не превышаем общий потолок блока — обрезаем по границе комментария.
            if (sb.Length + line.Length + 1 > MaxPriorCommentsBlockChars)
                break;
            sb.Append(line).Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    // Потолок блока реплик студента в prompt'е (#713): защита от аномально длинного
    // треда, чтобы не раздувать input tokens. Обрезаем по границе сообщения.
    private const int MaxStudentRepliesBlockChars = 4000;

    // Bounded read под prompt-контекст: тянем не весь тред, а только последние N реплик
    // (свежие релевантнее для текущей итерации). Полный тред остаётся у панели проверки.
    private const int StudentRepliesPromptLimit = 30;

    /// <summary>
    ///     Форматирует реплики студента в этом PR (#713) для prompt-блока <c>#0d</c>: по
    ///     каждому сообщению — тело + (<c>path:line</c> если есть) + отметка «автор ответил».
    ///     Включает ТОЛЬКО сообщения, созданные ДО <paramref name="before"/> (начало текущего
    ///     запуска итерации), по возрастанию времени. Грузим bounded-набор последних
    ///     <see cref="StudentRepliesPromptLimit"/> реплик; при переполнении char-cap оставляем
    ///     САМЫЕ СВЕЖИЕ (они относятся к текущему состоянию PR). Пустой результат (реплик нет)
    ///     → null, тогда блок в prompt не добавляется.
    /// </summary>
    private async Task<string?> BuildStudentRepliesContextAsync(
        Guid aiReviewId, DateTimeOffset before, CancellationToken ct)
    {
        IReadOnlyList<StudentPrMessage> messages =
            await _messages.GetRecentByAiReviewIdAsync(aiReviewId, StudentRepliesPromptLimit, ct);

        // Строки в хронологическом порядке (messages уже ascending после разворота в репо).
        List<string> lines = [];
        foreach (StudentPrMessage m in messages)
        {
            if (m.CreatedAtGithub >= before || string.IsNullOrWhiteSpace(m.Body))
                continue;

            string location = string.IsNullOrEmpty(m.Path)
                ? string.Empty
                : $" ({m.Path}:{(m.Line?.ToString(CultureInfo.InvariantCulture) ?? "?")})";
            string answered = m.AnsweredAt is not null ? " [автор уже ответил]" : string.Empty;
            lines.Add($"- {m.Body.Trim()}{location}{answered}");
        }

        if (lines.Count == 0)
            return null;

        // Char-cap: идём от самой свежей реплики к старым, оставляя свежие. Самую свежую
        // включаем всегда (даже если одна превышает cap) — иначе потеряли бы актуальный контекст.
        List<string> selected = [];
        int total = 0;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            int cost = lines[i].Length + 1;
            if (selected.Count > 0 && total + cost > MaxStudentRepliesBlockChars)
                break;
            selected.Add(lines[i]);
            total += cost;
        }

        selected.Reverse(); // обратно в хронологический порядок для промпта
        return string.Join('\n', selected);
    }

    /// <summary>
    ///     True если (path, line) — позиция, которую GitHub примет как inline review
    ///     comment: файл присутствует в diff'е, а строка попадает в RIGHT-side
    ///     (new-file) диапазон какого-либо hunk'а. Если файл в diff'е, но hunk'и не
    ///     распарсены (нет patch'а / chunked-путь) — валидировать нечем, разрешаем и
    ///     полагаемся на summary-only fallback при 422 (#368). Файла нет в diff'е →
    ///     позиция заведомо не-commentable (GitHub вернёт 422).
    /// </summary>
    private static bool IsCommentablePosition(string path, int line, VcsDiff diff)
    {
        VcsDiffFile? file = diff.Files
            .FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.Ordinal));
        if (file is null) return false;
        if (file.Hunks.Count == 0) return true;
        return file.Hunks.Any(h => line >= h.NewStart && line < h.NewStart + h.NewLines);
    }

    private static Error MapVcsError(Error vcsError, string repoFullName)
    {
        string code = vcsError.Messages[0].Code;
        return code switch
        {
            "vcs.repo.not_in_installation" => ReviewErrors.RepoNotInInstallation(repoFullName),
            "vcs.unauthorized" => ReviewErrors.GitHubUnavailable("authorization rejected"),
            "vcs.pull_request.not_found" => ReviewErrors.GitHubUnavailable("PR not found"),
            // 4xx от GitHub (#361) — наш payload отвергнут. Ретрай того же запроса
            // не поможет, нужен новый LLM-вывод (через «Перепроверить»).
            "vcs.invalid_request" => ReviewErrors.GitHubInvalidRequest(vcsError.Messages[0].Message),
            "vcs.unavailable" => ReviewErrors.GitHubUnavailable(vcsError.Messages[0].Message),
            "vcs.response.malformed" => ReviewErrors.GitHubUnavailable(vcsError.Messages[0].Message),
            _ => ReviewErrors.GitHubUnavailable(vcsError.Messages[0].Message),
        };
    }
}
