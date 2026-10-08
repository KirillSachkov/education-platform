using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.Infrastructure.GitHub.Models;
using Microsoft.Extensions.Logging;
using Shared.GitHubApp;

namespace AssignmentReviewService.Infrastructure.GitHub;

public sealed class GitHubVcsProvider : IVcsProvider
{
    // GitHub caps the PR-files / compare-files endpoints at 100 entries per page.
    // Без пагинации PR с >100 изменёнными файлами молча отдавал partial diff (первые
    // 100 файлов в алфавитном порядке) — модель не видела файлы во второй половине
    // и выносила ложный «X отсутствует» вердикт (прод-кейс owlillp/DirectoryService#38:
    // 251 backend-файл сортируется раньше frontend/ → frontend выпадал целиком).
    private const int FILES_PER_PAGE = 100;

    // GitHub отдаёт максимум 3000 файлов на PR (30 страниц). За этим потолком diff
    // в любом случае выше hard-cap'ов DiffChunker'а — обрубаем с warning'ом, чтобы
    // не зациклиться на нестандартном ответе.
    private const int MAX_FILE_PAGES = 30;

    private readonly HttpClient _http;
    private readonly IGitHubAppTokenService _tokens;
    private readonly ILogger<GitHubVcsProvider> _logger;

    public GitHubVcsProvider(
        HttpClient http,
        IGitHubAppTokenService tokens,
        ILogger<GitHubVcsProvider> logger)
    {
        _http = http;
        _tokens = tokens;
        _logger = logger;
    }

    public VcsProvider Provider => VcsProvider.GITHUB;

    public Task<Result<VcsPullRequest, Error>> GetPullRequestAsync(
        string installationId, string repoFullName, int pullNumber, CancellationToken ct = default) =>
        SendAsync<GitHubPullDto, VcsPullRequest>(
            installationId,
            HttpMethod.Get,
            $"repos/{repoFullName}/pulls/{pullNumber.ToString(CultureInfo.InvariantCulture)}",
            null,
            dto => new VcsPullRequest(
                repoFullName,
                dto.Number,
                dto.Title,
                dto.User.Login,
                dto.Head.Sha,
                dto.Head.Ref,
                dto.Base.Ref,
                dto.HtmlUrl,
                dto.State,
                dto.Draft),
            notFoundError: () => VcsErrors.PullRequestNotFound(repoFullName, pullNumber),
            ct: ct);

    public async Task<Result<VcsDiff, Error>> GetPullRequestDiffAsync(
        string installationId, string repoFullName, int pullNumber, CancellationToken ct = default)
    {
        Result<VcsPullRequest, Error> prResult =
            await GetPullRequestAsync(installationId, repoFullName, pullNumber, ct);
        if (prResult.IsFailure) return prResult.Error;
        VcsPullRequest pr = prResult.Value;

        // Пагинируем files endpoint — иначе PR с >100 файлами теряет хвост (см. FILES_PER_PAGE).
        List<GitHubFileDto> allFiles = [];
        for (int page = 1; page <= MAX_FILE_PAGES; page++)
        {
            Result<List<GitHubFileDto>, Error> pageResult =
                await SendAsync<List<GitHubFileDto>, List<GitHubFileDto>>(
                    installationId,
                    HttpMethod.Get,
                    $"repos/{repoFullName}/pulls/{pullNumber.ToString(CultureInfo.InvariantCulture)}/files"
                        + $"?per_page={FILES_PER_PAGE.ToString(CultureInfo.InvariantCulture)}"
                        + $"&page={page.ToString(CultureInfo.InvariantCulture)}",
                    null,
                    x => x,
                    notFoundError: () => VcsErrors.PullRequestNotFound(repoFullName, pullNumber),
                    ct: ct);
            if (pageResult.IsFailure) return pageResult.Error;

            allFiles.AddRange(pageResult.Value);

            // Неполная страница → файлов больше нет.
            if (pageResult.Value.Count < FILES_PER_PAGE)
                break;

            if (page == MAX_FILE_PAGES)
                _logger.LogWarning(
                    "PR {Repo}#{PullNumber} has more than {Max} changed files — diff fetch capped at that many; review may be incomplete.",
                    repoFullName,
                    pullNumber,
                    MAX_FILE_PAGES * FILES_PER_PAGE);
        }

        return BuildDiff(pr.HeadSha, allFiles);
    }

    public async Task<Result<VcsDiff, Error>> CompareAsync(
        string installationId,
        string repoFullName,
        string baseSha,
        string headSha,
        CancellationToken ct = default)
    {
        // GET /repos/{owner}/{repo}/compare/{base}...{head} — incremental diff (#17).
        // Тот же per_page cap (100 файлов) и тот же per-file shape, что и diff endpoint —
        // пагинируем по тем же причинам (within-review re-review может нести >100 файлов).
        // NB: page-пагинация files-массива у compare endpoint'а недокументирована (docs
        // описывают per_page для commits, files capped ~300). Но fallback безопасен: если
        // GitHub проигнорит page, последняя страница вернётся неполной (< FILES_PER_PAGE) и
        // цикл прервётся — без зацикливания, независимо от реального поведения API.
        string range =
            $"{Uri.EscapeDataString(baseSha)}...{Uri.EscapeDataString(headSha)}";

        List<GitHubFileDto> allFiles = [];
        for (int page = 1; page <= MAX_FILE_PAGES; page++)
        {
            Result<GitHubCompareDto, Error> pageResult =
                await SendAsync<GitHubCompareDto, GitHubCompareDto>(
                    installationId,
                    HttpMethod.Get,
                    $"repos/{repoFullName}/compare/{range}"
                        + $"?per_page={FILES_PER_PAGE.ToString(CultureInfo.InvariantCulture)}"
                        + $"&page={page.ToString(CultureInfo.InvariantCulture)}",
                    null,
                    x => x,
                    notFoundError: () => VcsErrors.ResourceNotFound(
                        $"{repoFullName} compare {baseSha}...{headSha}"),
                    ct: ct);
            if (pageResult.IsFailure) return pageResult.Error;

            List<GitHubFileDto> pageFiles = pageResult.Value.Files ?? [];
            allFiles.AddRange(pageFiles);

            if (pageFiles.Count < FILES_PER_PAGE)
                break;

            if (page == MAX_FILE_PAGES)
                _logger.LogWarning(
                    "Compare {Repo} {Base}...{Head} has more than {Max} changed files — diff fetch capped; review may be incomplete.",
                    repoFullName,
                    baseSha,
                    headSha,
                    MAX_FILE_PAGES * FILES_PER_PAGE);
        }

        return BuildDiff(headSha, allFiles);
    }

    private static VcsDiff BuildDiff(string headSha, List<GitHubFileDto> files)
    {
        List<VcsDiffFile> diffFiles = files
            .Select(f => new VcsDiffFile(
                f.Filename,
                f.PreviousFilename,
                f.Status,
                f.Additions,
                f.Deletions,
                f.Patch,
                ParseHunks(f.Patch)))
            .ToList();

        return new VcsDiff(
            headSha,
            diffFiles,
            diffFiles.Sum(f => f.Additions),
            diffFiles.Sum(f => f.Deletions));
    }

    public Task<Result<VcsPostedReview, Error>> PostReviewAsync(
        string installationId,
        string repoFullName,
        int pullNumber,
        VcsReviewRequest review,
        CancellationToken ct = default)
    {
        GitHubReviewRequestDto body = new(
            review.CommitSha,
            review.SummaryBody,
            "COMMENT",
            review.InlineComments
                .Select(c => new GitHubReviewCommentDto(
                    c.Path,
                    c.Line,
                    "RIGHT",
                    c.Suggestion is null
                        ? c.Body
                        : $"{c.Body}\n\n```suggestion\n{c.Suggestion}\n```"))
                .ToList());

        return SendAsync<GitHubReviewResponseDto, VcsPostedReview>(
            installationId,
            HttpMethod.Post,
            $"repos/{repoFullName}/pulls/{pullNumber.ToString(CultureInfo.InvariantCulture)}/reviews",
            JsonContent.Create(body),
            dto => new VcsPostedReview(dto.Id, dto.HtmlUrl),
            notFoundError: () => VcsErrors.PullRequestNotFound(repoFullName, pullNumber),
            ct: ct);
    }

    public Task<Result<VcsPostedComment, Error>> PostCommentReplyAsync(
        string installationId,
        string repoFullName,
        int pullNumber,
        StudentPrMessageKind kind,
        long inReplyToCommentId,
        string body,
        CancellationToken ct = default)
    {
        string pull = pullNumber.ToString(CultureInfo.InvariantCulture);

        // REVIEW_COMMENT → reply в тот же тред inline-коммента; ISSUE_COMMENT → новый
        // top-level коммент PR (у issue_comment-события нет reply-семантики на GitHub).
        string path = kind == StudentPrMessageKind.REVIEW_COMMENT
            ? $"repos/{repoFullName}/pulls/{pull}/comments/{inReplyToCommentId.ToString(CultureInfo.InvariantCulture)}/replies"
            : $"repos/{repoFullName}/issues/{pull}/comments";

        return SendAsync<GitHubCommentResponseDto, VcsPostedComment>(
            installationId,
            HttpMethod.Post,
            path,
            JsonContent.Create(new GitHubCommentBodyDto(body)),
            dto => new VcsPostedComment(dto.Id, dto.HtmlUrl),
            notFoundError: () => VcsErrors.PullRequestNotFound(repoFullName, pullNumber),
            ct: ct);
    }

    public Task<Result<IReadOnlyList<VcsReviewComment>, Error>> GetReviewCommentsAsync(
        string installationId,
        string repoFullName,
        int pullNumber,
        long reviewId,
        CancellationToken ct = default) =>
        // GET /repos/{owner}/{repo}/pulls/{pull}/reviews/{review_id}/comments — inline
        // comments конкретного review'а (#383). per_page cap 100; у одной итерации
        // комментов всегда ≤ MaxInlineComments (десятки), пагинация не нужна.
        SendAsync<List<GitHubReviewCommentResponseDto>, IReadOnlyList<VcsReviewComment>>(
            installationId,
            HttpMethod.Get,
            $"repos/{repoFullName}/pulls/{pullNumber.ToString(CultureInfo.InvariantCulture)}"
                + $"/reviews/{reviewId.ToString(CultureInfo.InvariantCulture)}/comments?per_page=100",
            null,
            dtos => (IReadOnlyList<VcsReviewComment>)dtos
                .Where(c => !string.IsNullOrWhiteSpace(c.Body))
                .Select(c => new VcsReviewComment(
                    c.Path ?? string.Empty,
                    c.Line ?? c.OriginalLine ?? 0,
                    c.Body))
                .ToList(),
            notFoundError: () => VcsErrors.ResourceNotFound(
                $"{repoFullName}#{pullNumber} review {reviewId.ToString(CultureInfo.InvariantCulture)}"),
            ct: ct);

    public Task<Result<IReadOnlyList<VcsRepoTreeEntry>, Error>> GetRepoTreeAsync(
        string installationId, string repoFullName, string branch, CancellationToken ct = default) =>
        SendAsync<GitHubTreeResponseDto, IReadOnlyList<VcsRepoTreeEntry>>(
            installationId,
            HttpMethod.Get,
            $"repos/{repoFullName}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1",
            null,
            dto =>
            {
                if (dto.Truncated)
                {
                    // GitHub truncates recursive tree responses past 100K entries.
                    // Indexer (Phase 6) проверит коллекцию и решит fallback на
                    // git clone; пока — warning в лог.
                    _logger.LogWarning(
                        "GitHub tree for {Repo}/{Branch} is truncated (>100K entries). RAG indexer might miss files.",
                        repoFullName,
                        branch);
                }

                return (IReadOnlyList<VcsRepoTreeEntry>)dto.Tree
                    .Select(e => new VcsRepoTreeEntry(e.Path, e.Type, e.Size, e.Sha))
                    .ToList();
            },
            notFoundError: () => VcsErrors.ResourceNotFound($"{repoFullName} tree at branch '{branch}'"),
            ct: ct);

    public Task<Result<VcsFileContent, Error>> GetFileContentAsync(
        string installationId,
        string repoFullName,
        string path,
        string? branch = null,
        CancellationToken ct = default)
    {
        // Each path segment is URL-encoded individually; полный
        // Uri.EscapeDataString закодировал бы '/' разделители.
        string encodedPath = string.Join(
            "/",
            path.Split('/').Select(Uri.EscapeDataString));
        string queryString = branch is null
            ? string.Empty
            : $"?ref={Uri.EscapeDataString(branch)}";

        return SendAsync<GitHubFileContentDto, VcsFileContent>(
            installationId,
            HttpMethod.Get,
            $"repos/{repoFullName}/contents/{encodedPath}{queryString}",
            null,
            dto => new VcsFileContent(dto.Path, dto.Sha, dto.Size, dto.Encoding, dto.Content),
            notFoundError: () => VcsErrors.ResourceNotFound($"{repoFullName}:{path}"),
            ct: ct);
    }

    public async Task<Result<VcsInstallationDetail, Error>> GetInstallationDetailAsync(
        long installationId, CancellationToken ct = default)
    {
        // GET /app/installations/{id} requires App-level JWT, NOT installation token
        // (GitHub returns 401 if you send a installation token here). For the
        // /installation/repositories listing below, installation token is correct.
        Result<string, Error> jwtResult = _tokens.GetAppJwt();
        if (jwtResult.IsFailure) return jwtResult.Error;

        Result<GitHubInstallationDetailDto, Error> detailResult =
            await SendWithBearerAsync<GitHubInstallationDetailDto>(
                jwtResult.Value,
                HttpMethod.Get,
                $"app/installations/{installationId.ToString(CultureInfo.InvariantCulture)}",
                ct);
        if (detailResult.IsFailure) return detailResult.Error;
        GitHubInstallationDetailDto detail = detailResult.Value;

        Result<string, Error> tokenResult = await _tokens.GetInstallationTokenAsync(installationId, ct);
        if (tokenResult.IsFailure) return tokenResult.Error;

        VcsInstallationOwnerType ownerType = string.Equals(
            detail.Account.Type,
            "Organization",
            StringComparison.OrdinalIgnoreCase)
                ? VcsInstallationOwnerType.ORG
                : VcsInstallationOwnerType.USER;

        RepoSelections repoSelections;
        if (string.Equals(detail.RepositorySelection, "all", StringComparison.OrdinalIgnoreCase))
        {
            repoSelections = RepoSelections.AllRepos();
        }
        else
        {
            // Selected — fetch конкретный whitelist через installation token.
            Result<GitHubInstallationRepositoriesDto, Error> reposResult =
                await SendWithTokenAsync<GitHubInstallationRepositoriesDto>(
                    tokenResult.Value,
                    HttpMethod.Get,
                    "installation/repositories?per_page=100",
                    ct);
            if (reposResult.IsFailure) return reposResult.Error;

            // GitHub caps per_page at 100; PRoject Phase 4 не поддерживает > 100
            // выбранных репозиториев в whitelist (typical case ≤ 30). Будет
            // pagination в Phase 7+ если потребуется.
            repoSelections = RepoSelections.Specific(
                reposResult.Value.Repositories.Select(r => r.FullName).ToList());
        }

        return new VcsInstallationDetail(
            detail.Account.Login,
            detail.Account.Id.ToString(CultureInfo.InvariantCulture),
            ownerType,
            repoSelections);
    }

    private Task<Result<TDto, Error>> SendWithTokenAsync<TDto>(
        string installationToken,
        HttpMethod method,
        string path,
        CancellationToken ct)
        => SendWithAuthAsync<TDto>(
            new AuthenticationHeaderValue("token", installationToken), method, path, ct);

    private Task<Result<TDto, Error>> SendWithBearerAsync<TDto>(
        string bearerToken,
        HttpMethod method,
        string path,
        CancellationToken ct)
        => SendWithAuthAsync<TDto>(
            new AuthenticationHeaderValue("Bearer", bearerToken), method, path, ct);

    private async Task<Result<TDto, Error>> SendWithAuthAsync<TDto>(
        AuthenticationHeaderValue auth,
        HttpMethod method,
        string path,
        CancellationToken ct)
    {
        using HttpRequestMessage req = new(method, path);
        req.Headers.Authorization = auth;
        req.Headers.UserAgent.ParseAdd("sachkov-learn/assignment-review");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.NotFound)
                return VcsErrors.ResourceNotFound(path);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return VcsErrors.UnauthorizedFromVcs();

            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                // 1.6 hardening (#264): 403 с X-RateLimit-Remaining: 0 — secondary
                // rate-limit (semantically 429 в новых документациях GitHub).
                if (IsSecondaryRateLimit(resp))
                {
                    int? retryAfter = ParseRetryAfter(resp);
                    _logger.LogWarning(
                        "GitHub API {Method} {Path} 403 secondary rate-limit (retry_after={RetryAfter}s).",
                        method,
                        path,
                        retryAfter);
                    return VcsErrors.RateLimited(retryAfter);
                }

                _logger.LogWarning(
                    "GitHub API {Method} {Path} returned 403 (permission scope, token kept).",
                    method,
                    path);
                return VcsErrors.UnauthorizedFromVcs();
            }

            if ((int)resp.StatusCode == 429)
            {
                int? retryAfter = ParseRetryAfter(resp);
                _logger.LogWarning(
                    "GitHub API {Method} {Path} 429 (retry_after={RetryAfter}s).",
                    method,
                    path,
                    retryAfter);
                return VcsErrors.RateLimited(retryAfter);
            }

            if (!resp.IsSuccessStatusCode)
            {
                return await MapNonSuccessAsync(resp, method, path, ct);
            }

            TDto? dto = await resp.Content.ReadFromJsonAsync<TDto>(ct);
            return dto is null
                ? VcsErrors.MalformedResponse("empty body")
                : Result.Success<TDto, Error>(dto);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout talking to GitHub {Method} {Path}", method, path);
            return VcsErrors.VcsUnavailable(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error talking to GitHub {Method} {Path}", method, path);
            return VcsErrors.VcsUnavailable(ex.Message);
        }
        catch (JsonException ex)
        {
            return VcsErrors.MalformedResponse(ex.Message);
        }
    }

    private async Task<Result<TResult, Error>> SendAsync<TDto, TResult>(
        string installationId,
        HttpMethod method,
        string path,
        HttpContent? content,
        Func<TDto, TResult> map,
        Func<Error> notFoundError,
        CancellationToken ct)
    {
        if (!long.TryParse(installationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long installationIdLong))
            return VcsErrors.InstallationIdInvalid(installationId);

        Result<string, Error> tokenResult = await _tokens.GetInstallationTokenAsync(installationIdLong, ct);
        if (tokenResult.IsFailure) return tokenResult.Error;

        using HttpRequestMessage req = new(method, path);
        if (content is not null) req.Content = content;
        req.Headers.Authorization = new AuthenticationHeaderValue("token", tokenResult.Value);
        req.Headers.UserAgent.ParseAdd("sachkov-learn/assignment-review");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);

            // 401: токен отозван / installation suspended → инвалидируем кэш.
            // 403: GitHub использует и для secondary rate-limit (X-RateLimit-Remaining=0),
            // и для permission-scope mismatch — оба случая token остаётся валидным,
            // не зачем эвиктить кэш. AccessService ходит тем же путём (только 401).
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                _tokens.Invalidate(installationIdLong);
                return VcsErrors.UnauthorizedFromVcs();
            }

            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                // 1.6 hardening (#264): 403 с X-RateLimit-Remaining: 0 — secondary
                // rate-limit. Surface отдельным кодом, чтобы caller-у (RunIteration UI)
                // показать «попробуй через N секунд», а не общую auth-ошибку.
                if (IsSecondaryRateLimit(resp))
                {
                    int? retryAfter = ParseRetryAfter(resp);
                    _logger.LogWarning(
                        "GitHub API {Method} {Path} 403 secondary rate-limit (retry_after={RetryAfter}s).",
                        method,
                        path,
                        retryAfter);
                    return VcsErrors.RateLimited(retryAfter);
                }

                _logger.LogWarning(
                    "GitHub API {Method} {Path} returned 403 (permission scope, token kept).",
                    method,
                    path);
                return VcsErrors.UnauthorizedFromVcs();
            }

            if ((int)resp.StatusCode == 429)
            {
                int? retryAfter = ParseRetryAfter(resp);
                _logger.LogWarning(
                    "GitHub API {Method} {Path} 429 (retry_after={RetryAfter}s).",
                    method,
                    path,
                    retryAfter);
                return VcsErrors.RateLimited(retryAfter);
            }

            if (resp.StatusCode == HttpStatusCode.NotFound)
                return notFoundError();

            if (!resp.IsSuccessStatusCode)
            {
                return await MapNonSuccessAsync(resp, method, path, ct);
            }

            TDto? dto = await resp.Content.ReadFromJsonAsync<TDto>(ct);
            return dto is null
                ? VcsErrors.MalformedResponse("empty body")
                : map(dto);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout talking to GitHub {Method} {Path}", method, path);
            return VcsErrors.VcsUnavailable(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error talking to GitHub {Method} {Path}", method, path);
            return VcsErrors.VcsUnavailable(ex.Message);
        }
        catch (JsonException ex)
        {
            return VcsErrors.MalformedResponse(ex.Message);
        }
    }

    /// <summary>
    ///     Map non-2xx GitHub responses that weren't already handled by status-code
    ///     branches (401/403/404/429) into a domain VCS error. 4xx → <c>vcs.invalid_request</c>
    ///     (наш payload, ретрай того же запроса не поможет); 5xx и прочее →
    ///     <c>vcs.unavailable</c> (GitHub лежит, ретрай уместен).
    ///
    ///     Тело ответа GitHub читается с потолком 2000 символов и попадает в Warning-лог:
    ///     без него 4xx-причины (e.g. inline-комментарий вне diff'а на POST /reviews → 422)
    ///     были неотлаживаемы — issue #361.
    /// </summary>
    private async Task<Error> MapNonSuccessAsync(
        HttpResponseMessage resp,
        HttpMethod method,
        string path,
        CancellationToken ct)
    {
        int status = (int)resp.StatusCode;
        string bodyForLog = await ReadBoundedBodyAsync(resp, ct);
        // ai_review_iterations.failure_reason — VARCHAR(500). Detail попадает туда через
        // ReviewErrors.GitHubInvalidRequest message, поэтому ужимаем агрессивнее, чем
        // лог: лог хранит полный 2000-char body, а в БД летит только короткая выжимка.
        string bodyForError = TruncateForError(bodyForLog);

        if (status is >= 400 and < 500)
        {
            _logger.LogWarning(
                "GitHub API {Method} {Path} rejected request with {Status}: {Body}",
                method,
                path,
                status,
                bodyForLog);
            return VcsErrors.VcsInvalidRequest($"HTTP {status}: {bodyForError}");
        }

        _logger.LogWarning(
            "GitHub API {Method} {Path} returned {Status}: {Body}",
            method,
            path,
            status,
            bodyForLog);
        return VcsErrors.VcsUnavailable($"HTTP {status}: {bodyForError}");
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        const int MaxChars = 2000;
        try
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return "<empty>";
            return body.Length <= MaxChars ? body : body[..MaxChars] + "...<truncated>";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ObjectDisposedException)
        {
            return $"<body read failed: {ex.Message}>";
        }
    }

    private static string TruncateForError(string body)
    {
        const int MaxChars = 200;
        return body.Length <= MaxChars ? body : body[..MaxChars] + "...";
    }

    /// <summary>
    ///     Detect GitHub's secondary rate-limit signature on a 403 response:
    ///     <c>X-RateLimit-Remaining: 0</c>. Primary 429 is handled by status-code
    ///     branch directly.
    /// </summary>
    private static bool IsSecondaryRateLimit(HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? values))
            return false;

        string? first = values.FirstOrDefault();
        return string.Equals(first, "0", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Parse <c>Retry-After</c> (seconds OR HTTP-date) and fallback to
    ///     <c>X-RateLimit-Reset</c> (Unix epoch seconds). Returns seconds-until-retry
    ///     или <c>null</c> если ни один header не присутствует / не парсится.
    /// </summary>
    private static int? ParseRetryAfter(HttpResponseMessage resp)
    {
        // RFC 7231 §7.1.3 — Retry-After: либо <delta-seconds>, либо HTTP-date.
        if (resp.Headers.RetryAfter is { } ra)
        {
            if (ra.Delta is { } delta)
                return (int)Math.Ceiling(delta.TotalSeconds);
            if (ra.Date is { } until)
            {
                double secs = (until - DateTimeOffset.UtcNow).TotalSeconds;
                if (secs > 0) return (int)Math.Ceiling(secs);
            }
        }

        // GitHub-specific: X-RateLimit-Reset = Unix epoch seconds of reset.
        if (resp.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string>? values))
        {
            string? raw = values.FirstOrDefault();
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long epoch))
            {
                DateTimeOffset resetAt = DateTimeOffset.FromUnixTimeSeconds(epoch);
                double secs = (resetAt - DateTimeOffset.UtcNow).TotalSeconds;
                if (secs > 0) return (int)Math.Ceiling(secs);
            }
        }

        return null;
    }

    private static IReadOnlyList<VcsHunk> ParseHunks(string? patch)
    {
        if (string.IsNullOrEmpty(patch)) return [];

        List<VcsHunk> hunks = [];
        StringBuilder body = new();
        int oldStart = 0, oldLines = 0, newStart = 0, newLines = 0;
        bool inHunk = false;

        foreach (string line in patch.Split('\n'))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (inHunk)
                    hunks.Add(new VcsHunk(oldStart, oldLines, newStart, newLines, body.ToString()));
                body.Clear();
                body.Append(line).Append('\n');
                ParseHunkHeader(line, out oldStart, out oldLines, out newStart, out newLines);
                inHunk = true;
            }
            else if (inHunk)
            {
                body.Append(line).Append('\n');
            }
        }

        if (inHunk)
            hunks.Add(new VcsHunk(oldStart, oldLines, newStart, newLines, body.ToString()));

        return hunks;
    }

    private static void ParseHunkHeader(
        string line, out int oldStart, out int oldLines, out int newStart, out int newLines)
    {
        oldStart = oldLines = newStart = newLines = 0;
        int dashIdx = line.IndexOf('-', StringComparison.Ordinal);
        if (dashIdx < 0) return;
        int plusIdx = line.IndexOf('+', dashIdx);
        if (plusIdx < 0) return;
        int atatEnd = line.IndexOf(" @@", plusIdx, StringComparison.Ordinal);
        if (atatEnd < 0) return;

        string oldRange = line.Substring(dashIdx + 1, plusIdx - dashIdx - 2).Trim();
        string newRange = line.Substring(plusIdx + 1, atatEnd - plusIdx - 1).Trim();
        ParseRange(oldRange, out oldStart, out oldLines);
        ParseRange(newRange, out newStart, out newLines);
    }

    private static void ParseRange(string range, out int start, out int lines)
    {
        int comma = range.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            int.TryParse(range, NumberStyles.Integer, CultureInfo.InvariantCulture, out start);
            lines = 1;
        }
        else
        {
            int.TryParse(range[..comma], NumberStyles.Integer, CultureInfo.InvariantCulture, out start);
            int.TryParse(range[(comma + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out lines);
        }
    }
}
