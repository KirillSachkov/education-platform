using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Installations.Services;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using Shared.GitHubApp;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using StackExchange.Redis;
using GitHubAppOptions = AssignmentReviewService.Core.Features.Installations.Services.GitHubAppOptions;

namespace AssignmentReviewService.Core.Features.Webhooks.UseCases;

/// <summary>
///     Принимает webhook'и от ARS GitHub App. Whitelist'нутые events:
///     <c>installation</c> (created / deleted / suspend / unsuspend),
///     <c>installation_repositories</c> (added / removed) и — обратный канал к AI-ревью (#713) —
///     <c>pull_request_review_comment</c> / <c>issue_comment</c> (студент прокомментировал свой PR).
///     На <c>installation.created</c> без существующей строки выполняется recovery (#451): резолв
///     юзера через AuthService + создание <c>VcsInstallation</c> — чтобы установка не терялась,
///     если install-callback не отработал. Authentication: HMAC-SHA256 подпись body против
///     <c>WebhookSecret</c>.
/// </summary>
public sealed class HandleGitHubWebhookEndpoint : IEndpoint
{
    public const long MAX_BODY_BYTES = 5 * 1024 * 1024;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/assignment-review/webhooks/github",
                async (
                    HttpContext httpContext,
                    [Microsoft.AspNetCore.Mvc.FromServices] HandleGitHubWebhookHandler handler,
                    CancellationToken ct) =>
                {
                    using MemoryStream ms = new();
                    await httpContext.Request.Body.CopyToAsync(ms, ct);
                    byte[] body = ms.ToArray();

                    string? signature = httpContext.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
                    string? eventName = httpContext.Request.Headers["X-GitHub-Event"].FirstOrDefault();
                    string? deliveryId = httpContext.Request.Headers["X-GitHub-Delivery"].FirstOrDefault();

                    return await handler.HandleAsync(body, signature, eventName, deliveryId, ct);
                })
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("ar-github-webhook")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MAX_BODY_BYTES));
    }
}

public sealed class HandleGitHubWebhookHandler
{
    private const string DEDUP_KEY_PREFIX = "ars:webhook:dedup:";
    private static readonly TimeSpan DEDUP_TTL = TimeSpan.FromMinutes(10);

    // GitHub-кап на тело коммента — 65536 символов. Отсекаем сверх этого при ingest'е
    // (защита от unbounded body в БД / промпте); схема (text-колонка) не меняется.
    private const int MaxStudentCommentBodyChars = 65536;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IVcsInstallationsRepository _installations;
    private readonly IAiReviewsRepository _reviews;
    private readonly IStudentPrMessagesRepository _messages;
    private readonly IVcsProvider _vcsProvider;
    private readonly IOutboxService _outbox;
    private readonly IAuthServiceClient _authClient;
    private readonly ITransactionManager _transactions;
    private readonly TimeProvider _time;
    private readonly GitHubAppOptions _options;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<HandleGitHubWebhookHandler> _logger;

    public HandleGitHubWebhookHandler(
        IVcsInstallationsRepository installations,
        IAiReviewsRepository reviews,
        IStudentPrMessagesRepository messages,
        IVcsProvider vcsProvider,
        IOutboxService outbox,
        IAuthServiceClient authClient,
        ITransactionManager transactions,
        TimeProvider time,
        IOptions<GitHubAppOptions> options,
        ILogger<HandleGitHubWebhookHandler> logger,
        IConnectionMultiplexer? redis = null)
    {
        _installations = installations;
        _reviews = reviews;
        _messages = messages;
        _vcsProvider = vcsProvider;
        _outbox = outbox;
        _authClient = authClient;
        _transactions = transactions;
        _time = time;
        _options = options.Value;
        _redis = redis;
        _logger = logger;
    }

    public async Task<Microsoft.AspNetCore.Http.IResult> HandleAsync(
        byte[] body, string? signature, string? eventName, string? deliveryId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            _logger.LogError("AssignmentReview:GitHub:WebhookSecret not configured — rejecting all webhooks.");
            return Results.StatusCode(503);
        }

        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Webhook arrived without X-Hub-Signature-256 header.");
            return Results.Unauthorized();
        }

        if (!WebhookSignatureVerifier.Verify(signature, body, _options.WebhookSecret))
        {
            _logger.LogWarning(
                "Webhook signature mismatch (event={Event}, sig_preview={Preview}).",
                eventName,
                WebhookSignatureVerifier.PreviewSignature(signature));
            return Results.Unauthorized();
        }

        if (string.IsNullOrEmpty(eventName))
        {
            return Results.BadRequest();
        }

        // Redis хранит только marker УСПЕШНО завершённой доставки. Раньше SET NX
        // выполнялся до business processing: transient DB/API failure уже помечал delivery
        // обработанной, и retry GitHub отбрасывался следующие 10 минут.
        if (await IsDeliveryAlreadyProcessedAsync(deliveryId))
        {
            _logger.LogInformation(
                "Webhook replay ignored: {DeliveryId} (event={Event}).",
                deliveryId,
                eventName);
            return Results.Ok();
        }

        try
        {
            switch (eventName)
            {
                case "installation":
                    await HandleInstallationEventAsync(body, ct);
                    break;
                case "installation_repositories":
                    await HandleRepositoriesEventAsync(body, ct);
                    break;
                case "pull_request_review_comment":
                    await HandlePullRequestReviewCommentAsync(body, ct);
                    break;
                case "issue_comment":
                    await HandleIssueCommentAsync(body, ct);
                    break;
                default:
                    // Ignored event — return 200 to не лочить retry storm на стороне GitHub.
                    _logger.LogDebug("Ignored webhook event: {Event}", eventName);
                    break;
            }

            await MarkDeliveryProcessedAsync(deliveryId);
            return Results.Ok();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed webhook payload (event={Event})", eventName);
            return Results.BadRequest();
        }
    }

    private async Task<bool> IsDeliveryAlreadyProcessedAsync(string? deliveryId)
    {
        if (string.IsNullOrEmpty(deliveryId) || _redis is null)
            return false;

        try
        {
            IDatabase db = _redis.GetDatabase();
            return await db.KeyExistsAsync(DEDUP_KEY_PREFIX + deliveryId);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(
                ex,
                "Redis dedup unavailable, processing webhook without dedup (delivery_id={DeliveryId}).",
                deliveryId);
            return false;
        }
    }

    private async Task MarkDeliveryProcessedAsync(string? deliveryId)
    {
        if (string.IsNullOrEmpty(deliveryId) || _redis is null)
            return;

        try
        {
            IDatabase db = _redis.GetDatabase();
            await db.StringSetAsync(
                DEDUP_KEY_PREFIX + deliveryId,
                "1",
                DEDUP_TTL,
                when: When.NotExists);
        }
        catch (RedisException ex)
        {
            // Durable DB constraints keep mutating webhook handlers idempotent. Redis is an
            // optimization, so marking failure must not turn a successful GitHub delivery into 5xx.
            _logger.LogWarning(
                ex,
                "Redis dedup marker could not be stored (delivery_id={DeliveryId}).",
                deliveryId);
        }
    }

    private async Task HandleInstallationEventAsync(byte[] body, CancellationToken ct)
    {
        InstallationEventPayload? payload = JsonSerializer.Deserialize<InstallationEventPayload>(body, JsonOpts);
        if (payload?.Installation is null) return;

        string installationIdStr = payload.Installation.Id.ToString(CultureInfo.InvariantCulture);
        VcsInstallation? installation = await _installations.GetByAsync(
            i => i.Provider == VcsProvider.GITHUB && i.InstallationId == installationIdStr,
            ct);

        if (installation is null)
        {
            // Recovery (#451): GitHub шлёт installation.created на КАЖДУЮ установку, но
            // строку создаёт только install-callback (нужен валидный single-use state-token).
            // Если callback не отработал (истёкший/использованный state, прямая установка с
            // GitHub) — строки нет, и студент навсегда висит на «Установить GitHub App».
            // Создаём установку здесь, резолвя владельца через AuthService. Прочие action'ы
            // (deleted/suspend/unsuspend) без строки — нечего обновлять, игнорим.
            if (string.Equals(payload.Action, "created", StringComparison.Ordinal))
                await RecoverInstallationFromWebhookAsync(payload.Installation.Id, ct);
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        switch (payload.Action)
        {
            case "deleted":
                installation.MarkUninstalled(now);
                break;
            case "suspend":
                installation.Suspend();
                break;
            case "unsuspend":
                installation.Unsuspend();
                break;
            case "created":
                // Обычно noop — callback handler уже создал. На повторе apply Reactivate
                // (защищает от out-of-order webhook + callback).
                installation.Reactivate();
                break;
            default:
                _logger.LogDebug(
                    "Unhandled installation action: {Action} (installation_id={Id})",
                    payload.Action,
                    payload.Installation.Id);
                return;
        }

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to save installation status change: {Code}",
                saveResult.Error.Messages[0].Code);
            throw saveResult.Error.AsTransient().ToException();
        }

        _logger.LogInformation(
            "Installation event {Action} → installation_id={InstallationId}, status={Status}",
            payload.Action,
            payload.Installation.Id,
            installation.Status);
    }

    /// <summary>
    ///     Создаёт <see cref="VcsInstallation"/> из webhook'а <c>installation.created</c>,
    ///     когда install-callback не зафиксировал установку (#451). Резолвит платформенного
    ///     юзера по external id владельца GitHub-аккаунта через AuthService; если привязки нет
    ///     (установили на аккаунте, не привязанном к платформе) — пропускаем. Идемпотентно:
    ///     на гонке с callback'ом повторно не вставляем (unique <c>provider, installation_id</c>).
    /// </summary>
    private async Task RecoverInstallationFromWebhookAsync(long installationId, CancellationToken ct)
    {
        // Re-fetch detail (owner + repo selections) — тот же вызов, что делает callback.
        Result<VcsInstallationDetail, Error> detailResult =
            await _vcsProvider.GetInstallationDetailAsync(installationId, ct);
        if (detailResult.IsFailure)
        {
            _logger.LogWarning(
                "installation.created recovery: failed to fetch detail (id={Id}): {Code}",
                installationId,
                detailResult.Error.Messages[0].Code);
            throw detailResult.Error.ToException();
        }
        VcsInstallationDetail detail = detailResult.Value;

        // Recovery поддерживает только USER-установки (личный аккаунт студента — кейс #451):
        // owner_external_id у org'а — это id организации, а не GitHub user id из user_logins,
        // так что резолв по нему всё равно вернёт null. Callback линкует org-установку к
        // state.UserId, у webhook'а такого контекста нет — явно скипаем, без лишнего lookup'а.
        if (detail.OwnerType != VcsInstallationOwnerType.USER)
        {
            _logger.LogInformation(
                "installation.created recovery: skipping non-USER owner {OwnerType}={Owner} " +
                "(recovery резолвит только личные GitHub-аккаунты; installation_id={Id}).",
                detail.OwnerType,
                detail.OwnerLogin,
                installationId);
            return;
        }

        Guid? linkedUserId = await ResolvePlatformUserIdAsync(detail.OwnerExternalId, ct);
        if (linkedUserId is null)
        {
            _logger.LogInformation(
                "installation.created recovery: GitHub account {Owner} (external_id={ExternalId}) " +
                "is not linked to a platform user — skipping (installation_id={Id}).",
                detail.OwnerLogin,
                detail.OwnerExternalId,
                installationId);
            return;
        }

        // Idempotency: callback мог создать строку между нашим GetByAsync и сейчас
        // (AuthService-вызов добавил латентность). Повторно не вставляем.
        string installationIdStr = installationId.ToString(CultureInfo.InvariantCulture);
        bool exists = await _installations.ExistsAsync(
            i => i.Provider == VcsProvider.GITHUB && i.InstallationId == installationIdStr, ct);
        if (exists) return;

        VcsInstallation created = VcsInstallation.Create(
            VcsProvider.GITHUB,
            installationIdStr,
            detail.OwnerType,
            detail.OwnerLogin,
            detail.OwnerExternalId,
            linkedUserId.Value,
            detail.RepoSelections);
        await _installations.AddAsync(created, ct);

        // Issue #307: тот же event, что публикует callback — AccessService авто-завершает
        // GITHUB_REVIEW_APP onboarding step. Flush'ится атомарно с insert'ом в SaveChanges ниже.
        await _outbox.PublishAsync(new VcsInstallationCreated(
            UserId: linkedUserId.Value,
            InstallationId: installationId,
            OwnerLogin: detail.OwnerLogin,
            OwnerType: detail.OwnerType.ToString(),
            OccurredAt: _time.GetUtcNow()));

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            // Гонка с callback'ом: оба прошли ExistsAsync и вставили — unique
            // (provider, installation_id) отбил наш insert. Если строка теперь есть,
            // callback победил — benign no-op, не ошибка. Иначе — настоящий сбой записи.
            bool createdConcurrently = await _installations.ExistsAsync(
                i => i.Provider == VcsProvider.GITHUB && i.InstallationId == installationIdStr, ct);
            if (createdConcurrently)
            {
                _logger.LogInformation(
                    "installation.created recovery: row created concurrently (callback won the race), id={Id}.",
                    installationId);
                return;
            }

            _logger.LogError(
                "installation.created recovery: failed to persist (id={Id}): {Code}",
                installationId,
                saveResult.Error.Messages[0].Code);
            throw saveResult.Error.AsTransient().ToException();
        }

        _logger.LogInformation(
            "GitHub App installation recovered from webhook for user={UserId} on {OwnerType}={OwnerLogin} " +
            "(installation_id={InstallationId}) — install-callback had not recorded it.",
            linkedUserId.Value,
            detail.OwnerType,
            detail.OwnerLogin,
            installationId);
    }

    private async Task<Guid?> ResolvePlatformUserIdAsync(string ownerExternalId, CancellationToken ct)
    {
        Result<UserIdByGithubIdResponse, Error> result =
            await _authClient.GetUserIdByGithubExternalIdAsync(ownerExternalId, ct);
        if (result.IsFailure)
        {
            // AuthService недоступен — не создаём строку (вернёмся к ней на retry webhook'а
            // или когда юзер переустановит / нажмёт «Синхронизировать»). Fail-open по аптайму.
            _logger.LogWarning(
                "installation.created recovery: AuthService lookup failed for external_id={ExternalId}: {Code}",
                ownerExternalId,
                result.Error.Messages[0].Code);
            throw result.Error.ToException();
        }
        return result.Value.UserId;
    }

    private async Task HandleRepositoriesEventAsync(byte[] body, CancellationToken ct)
    {
        InstallationEventPayload? payload = JsonSerializer.Deserialize<InstallationEventPayload>(body, JsonOpts);
        if (payload?.Installation is null) return;

        string installationIdStr = payload.Installation.Id.ToString(CultureInfo.InvariantCulture);
        VcsInstallation? installation = await _installations.GetByAsync(
            i => i.Provider == VcsProvider.GITHUB && i.InstallationId == installationIdStr,
            ct);
        if (installation is null) return;

        // Re-fetch full installation detail вместо diff'а из payload — атомарнее
        // (избегаем рассинхрон если между added и removed events пришли out-of-order).
        Result<VcsInstallationDetail, Error> detailResult =
            await _vcsProvider.GetInstallationDetailAsync(payload.Installation.Id, ct);
        if (detailResult.IsFailure)
        {
            _logger.LogWarning(
                "installation_repositories: failed to refetch detail (id={Id}): {Code}",
                payload.Installation.Id,
                detailResult.Error.Messages[0].Code);
            throw detailResult.Error.ToException();
        }

        installation.UpdateRepoSelections(detailResult.Value.RepoSelections);
        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to save repo-selection update: {Code}",
                saveResult.Error.Messages[0].Code);
            throw saveResult.Error.AsTransient().ToException();
        }
    }

    // ── #713: обратный канал — комментарии студента в PR ──────────────────────────

    /// <summary>
    ///     <c>pull_request_review_comment</c> (action <c>created</c>) — inline / threaded reply
    ///     студента на строке diff'а. Несёт <c>path</c>/<c>line</c> + опц. <c>in_reply_to_id</c>.
    ///     Владелец PR (студент) — <c>pull_request.user.login</c>.
    /// </summary>
    private async Task HandlePullRequestReviewCommentAsync(byte[] body, CancellationToken ct)
    {
        PrReviewCommentPayload? payload = JsonSerializer.Deserialize<PrReviewCommentPayload>(body, JsonOpts);
        if (payload is null
            || !string.Equals(payload.Action, "created", StringComparison.Ordinal)
            || payload.Comment is null
            || payload.PullRequest is null
            || payload.Repository is null)
        {
            return;
        }

        await IngestStudentCommentAsync(
            repoFullName: payload.Repository.FullName,
            pullNumber: payload.PullRequest.Number,
            prOwnerLogin: payload.PullRequest.User?.Login,
            commentAuthorLogin: payload.Comment.User?.Login,
            kind: StudentPrMessageKind.REVIEW_COMMENT,
            gitHubCommentId: payload.Comment.Id,
            inReplyToGitHubId: payload.Comment.InReplyToId,
            body: payload.Comment.Body,
            path: payload.Comment.Path,
            line: payload.Comment.Line ?? payload.Comment.OriginalLine,
            commentUrl: payload.Comment.HtmlUrl,
            createdAtGithub: payload.Comment.CreatedAt,
            ct: ct);
    }

    /// <summary>
    ///     <c>issue_comment</c> (action <c>created</c>) — top-level коммент в ленте PR. Игнорим,
    ///     если <c>issue.pull_request</c> отсутствует (это обычный issue, не PR). Владелец PR —
    ///     <c>issue.user.login</c>. Без привязки к строке (<c>path</c>/<c>line</c> = null).
    /// </summary>
    private async Task HandleIssueCommentAsync(byte[] body, CancellationToken ct)
    {
        IssueCommentPayload? payload = JsonSerializer.Deserialize<IssueCommentPayload>(body, JsonOpts);
        if (payload is null
            || !string.Equals(payload.Action, "created", StringComparison.Ordinal)
            || payload.Comment is null
            || payload.Issue is null
            || payload.Repository is null)
        {
            return;
        }

        // Только PR-комментарии: у issue_comment на обычном issue нет issue.pull_request.
        if (payload.Issue.PullRequest is null)
        {
            _logger.LogDebug("issue_comment on a plain issue (not a PR) — ignored.");
            return;
        }

        await IngestStudentCommentAsync(
            repoFullName: payload.Repository.FullName,
            pullNumber: payload.Issue.Number,
            prOwnerLogin: payload.Issue.User?.Login,
            commentAuthorLogin: payload.Comment.User?.Login,
            kind: StudentPrMessageKind.ISSUE_COMMENT,
            gitHubCommentId: payload.Comment.Id,
            inReplyToGitHubId: null,
            body: payload.Comment.Body,
            path: null,
            line: null,
            commentUrl: payload.Comment.HtmlUrl,
            createdAtGithub: payload.Comment.CreatedAt,
            ct: ct);
    }

    /// <summary>
    ///     Общий ingest комментария студента: фильтр «только студент-владелец PR, не бот»,
    ///     привязка к <see cref="AiReview"/> по (repo, pull), идемпотентность по
    ///     <c>github_comment_id</c>, публикация <see cref="StudentPrQuestionAsked"/>. Любой отсев —
    ///     тихий no-op (200), чтобы GitHub не устраивал retry storm.
    /// </summary>
    private async Task IngestStudentCommentAsync(
        string? repoFullName,
        int pullNumber,
        string? prOwnerLogin,
        string? commentAuthorLogin,
        StudentPrMessageKind kind,
        long gitHubCommentId,
        long? inReplyToGitHubId,
        string? body,
        string? path,
        int? line,
        string? commentUrl,
        DateTimeOffset? createdAtGithub,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repoFullName) || string.IsNullOrEmpty(commentAuthorLogin))
        {
            return;
        }

        // Фильтр: ингестим только комментарий самого студента (владельца PR), не бота-ревьюера
        // и не постороннего. Владелец PR = автор сдачи, поэтому его логин авторитетен как «студент».
        bool isBot = commentAuthorLogin.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase);
        bool isPrOwner = !string.IsNullOrEmpty(prOwnerLogin)
            && string.Equals(commentAuthorLogin, prOwnerLogin, StringComparison.OrdinalIgnoreCase);
        if (isBot || !isPrOwner)
        {
            _logger.LogDebug(
                "PR comment by {Author} ignored (bot={IsBot}, pr_owner={Owner}) — not the student.",
                commentAuthorLogin,
                isBot,
                prOwnerLogin);
            return;
        }

        // Привязка к отслеживаемой сдаче: последний AiReview под этот PR. Нет → не наша сдача.
        AiReview? review = await _reviews.GetLatestByPullRequestAsync(
            VcsProvider.GITHUB, repoFullName, pullNumber, ct);
        if (review is null)
        {
            _logger.LogDebug(
                "PR comment on {Repo}#{Pull} has no tracked AiReview — ignored (comment_id={CommentId}).",
                repoFullName,
                pullNumber,
                gitHubCommentId);
            return;
        }

        // Идемпотентность: GitHub ретраит доставку. Повтор того же github_comment_id — no-op,
        // без второй записи и без повторной публикации события (unique-индекс — durable-гарантия).
        if (await _messages.ExistsByGitHubCommentIdAsync(gitHubCommentId, ct))
        {
            _logger.LogDebug(
                "StudentPrMessage for github_comment_id={CommentId} already ingested — skip.",
                gitHubCommentId);
            return;
        }

        DateTimeOffset commentCreatedAt = createdAtGithub ?? _time.GetUtcNow();
        string commentBody = Truncate(body ?? string.Empty, MaxStudentCommentBodyChars);
        string commentHtmlUrl = commentUrl ?? review.PullRequestUrl;

        StudentPrMessage message = StudentPrMessage.Create(
            aiReviewId: review.Id,
            gitHubCommentId: gitHubCommentId,
            inReplyToGitHubId: inReplyToGitHubId,
            kind: kind,
            authorGithubLogin: commentAuthorLogin,
            body: commentBody,
            path: path,
            line: line,
            commentUrl: commentHtmlUrl,
            createdAtGithub: commentCreatedAt,
            ingestedAt: _time.GetUtcNow());
        await _messages.AddAsync(message, ct);

        // StudentUserId — платформенный студент, авторитетно берётся из AiReview.UserId (владелец
        // сдачи = владелец PR = автор коммента). StudentName резолвим best-effort по этому id —
        // событие самодостаточно (потребителям не нужен обратный вызов за именем).
        string? studentName = await ResolveStudentNameAsync(review.UserId, ct);

        await _outbox.PublishAsync(new StudentPrQuestionAsked(
            StudentPrMessageId: message.Id,
            AiReviewId: review.Id,
            SubmissionId: review.SubmissionId,
            IssueId: review.IssueId,
            // ARS не хранит CourseId на AiReview — потребитель (при необходимости) резолвит курс
            // по IssueId. Контракт держит поле nullable ровно для этого случая.
            CourseId: null,
            AuthorId: review.AuthorId,
            StudentUserId: review.UserId == Guid.Empty ? null : review.UserId,
            StudentGithubLogin: commentAuthorLogin,
            StudentName: studentName,
            Body: commentBody,
            RepoFullName: review.RepoFullName,
            PullNumber: review.PullNumber,
            PullRequestUrl: review.PullRequestUrl,
            CommentUrl: commentHtmlUrl,
            Path: path,
            Line: line,
            GithubCommentId: gitHubCommentId,
            CreatedAt: commentCreatedAt));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            // Гонка на unique(github_comment_id): два одновременных webhook'а прошли pre-check,
            // второй INSERT отбит индексом. Benign — сообщение уже есть, спокойно выходим (no-op,
            // без повторной публикации). Возврат 200 — GitHub не должен ретраить.
            if (await _messages.ExistsByGitHubCommentIdAsync(gitHubCommentId, ct))
            {
                _logger.LogDebug(
                    "StudentPrMessage for github_comment_id={CommentId} created concurrently — idempotent no-op.",
                    gitHubCommentId);
                return;
            }

            _logger.LogWarning(
                "Failed to persist StudentPrMessage for github_comment_id={CommentId}: {Code}.",
                gitHubCommentId,
                save.Error.Messages[0].Code);
            throw save.Error.AsTransient().ToException();
        }

        _logger.LogInformation(
            "Ingested student PR comment (kind={Kind}, github_comment_id={CommentId}) on review {ReviewId} " +
            "→ StudentPrQuestionAsked published (author={AuthorId}).",
            kind,
            gitHubCommentId,
            review.Id,
            review.AuthorId);
    }

    /// <summary>
    ///     Best-effort резолв display-name студента по платформенному user id (для уведомления
    ///     автора). Недоступность AuthService / отсутствие имени → <c>null</c>: имя декоративно,
    ///     ingest не валится.
    /// </summary>
    private async Task<string?> ResolveStudentNameAsync(Guid studentUserId, CancellationToken ct)
    {
        if (studentUserId == Guid.Empty)
            return null;

        Result<IReadOnlyList<AuthUserLookupDto>, Error> result =
            await _authClient.GetUsersByIdsAsync([studentUserId], ct);
        if (result.IsFailure)
        {
            _logger.LogDebug(
                "Failed to resolve student name for {UserId}: {Code} — publishing without name.",
                studentUserId,
                result.Error.Messages[0].Code);
            return null;
        }

        return result.Value.FirstOrDefault(u => u.UserId == studentUserId)?.Name;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed class InstallationEventPayload
    {
        [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("installation")] public InstallationField? Installation { get; set; }
    }

    private sealed class InstallationField
    {
        [JsonPropertyName("id")] public long Id { get; set; }
    }

    // ── #713 webhook payloads (partial — только нужные поля) ───────────────────────

    private sealed class PrReviewCommentPayload
    {
        [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("comment")] public ReviewCommentField? Comment { get; set; }
        [JsonPropertyName("pull_request")] public PullRequestField? PullRequest { get; set; }
        [JsonPropertyName("repository")] public RepositoryField? Repository { get; set; }
    }

    private sealed class ReviewCommentField
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("in_reply_to_id")] public long? InReplyToId { get; set; }
        [JsonPropertyName("user")] public UserField? User { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("line")] public int? Line { get; set; }
        [JsonPropertyName("original_line")] public int? OriginalLine { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    private sealed class IssueCommentPayload
    {
        [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("comment")] public IssueCommentField? Comment { get; set; }
        [JsonPropertyName("issue")] public IssueField? Issue { get; set; }
        [JsonPropertyName("repository")] public RepositoryField? Repository { get; set; }
    }

    private sealed class IssueCommentField
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("user")] public UserField? User { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    private sealed class IssueField
    {
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("user")] public UserField? User { get; set; }

        // Присутствует (непустой объект) только когда issue_comment относится к PR.
        [JsonPropertyName("pull_request")] public PullRequestRef? PullRequest { get; set; }
    }

    private sealed class PullRequestRef
    {
        // Marker: наличие ключа issue.pull_request отличает PR от обычного issue.
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    }

    private sealed class PullRequestField
    {
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("user")] public UserField? User { get; set; }
    }

    private sealed class RepositoryField
    {
        [JsonPropertyName("full_name")] public string? FullName { get; set; }
    }

    private sealed class UserField
    {
        [JsonPropertyName("login")] public string? Login { get; set; }
    }
}
