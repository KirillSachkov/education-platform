using System.Text.Json;
using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Errors;
using AssignmentReviewService.Core.Features.Reviews.Models;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;
using Microsoft.Extensions.Options;
using Shared.AI;

namespace AssignmentReviewService.Core.Features.Reviews.Services;

/// <summary>
///     Top-level AI orchestrator: load spec/guidelines → build prompt → call LLM with
///     structured output → parse → return <see cref="ParsedAiReview"/>. Один retry
///     на invalid JSON (если provider отдал malformed payload — повторяем явно с
///     <c>response_format=json_schema</c>).
/// </summary>
public sealed class AiReviewer
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IAiClient _aiClient;
    private readonly IIssueReviewSpecsRepository _specs;
    private readonly IProjectReviewGuidelinesRepository _guidelines;
    private readonly IOptions<AssignmentReviewAiOptions> _options;
    private readonly IAssignmentReviewAiModelSettingsResolver _settingsResolver;
    private readonly ILogger<AiReviewer> _logger;

    public AiReviewer(
        IAiClient aiClient,
        IIssueReviewSpecsRepository specs,
        IProjectReviewGuidelinesRepository guidelines,
        IOptions<AssignmentReviewAiOptions> options,
        IAssignmentReviewAiModelSettingsResolver settingsResolver,
        ILogger<AiReviewer> logger)
    {
        _aiClient = aiClient;
        _specs = specs;
        _guidelines = guidelines;
        _options = options;
        _settingsResolver = settingsResolver;
        _logger = logger;
    }

    /// <summary>
    ///     Прогоняет AI-ревью diff'а. Если diff (после фильтрации non-reviewable
    ///     файлов) укладывается в один batch — один LLM call (как раньше). Если
    ///     больше — diff бьётся на последовательные batch'и (#18), каждый ревьюится
    ///     отдельно, результат агрегируется. Diff выше hard-cap'а — graceful fail.
    /// </summary>
    /// <param name="previousReviewContext">
    ///     Краткий контекст предыдущей completed-итерации (#17) — verdict + summary;
    ///     передаётся в prompt как TRUSTED "Previous review" блок.
    /// </param>
    /// <param name="onBatchProgress">
    ///     Liveness-heartbeat (#690): вызывается после каждого завершённого batch'а в
    ///     multi-batch прогоне, чтобы stale-watchdog не считал легитимно-долгое ревью
    ///     зависшим. Single-batch fast-path не дёргает (один LLM-вызов укладывается в
    ///     порог). Best-effort: ошибка heartbeat'а не валит ревью.
    /// </param>
    /// <param name="studentReplies">
    ///     Реплики студента к прошлым замечаниям в этом PR (#713) — форматированный список,
    ///     подаётся в prompt как контекст (обёрнут UNTRUSTED). null = реплик нет.
    /// </param>
    /// <param name="repoContext">
    ///     Контекст репозитория для цикла дозапроса файлов (#798): карта репо в промпте +
    ///     bounded need_files-цикл. null = фича выключена — поведение байт-в-байт прежнее
    ///     (один вызов, старая схема). Применяется только на single-batch пути; chunked
    ///     (multi-batch) ревью идёт по-старому.
    /// </param>
    public async Task<Result<ParsedAiReview, Error>> ReviewAsync(
        Guid issueId,
        VcsPullRequest pullRequest,
        VcsDiff diff,
        string? modelOverride = null,
        string? previousReviewContext = null,
        bool allowOversizedDiff = false,
        Func<CancellationToken, Task>? onBatchProgress = null,
        string? studentReplies = null,
        RepoReviewContext? repoContext = null,
        CancellationToken ct = default)
    {
        IssueReviewSpec? spec = await _specs.GetByAsync(s => s.IssueId == issueId, ct);
        Guid projectId = spec?.ProjectId ?? Guid.Empty;
        ProjectReviewGuidelines? guidelines = projectId == Guid.Empty
            ? null
            : await _guidelines.GetByAsync(g => g.ProjectId == projectId, ct);

        // Phase 11 (#15) — резолвим settings из cascade (override → DB → config).
        EffectiveSlot reviewer = await _settingsResolver.ResolveReviewerAsync(modelOverride, ct);
        // Feature A (#15) — global base review prompt (DB override → config default).
        string basePrompt = await _settingsResolver.ResolveReviewerBasePromptAsync(ct);

        AssignmentReviewLimits limits = _options.Value.Limits;
        // Manual runs get a larger ceiling than auto-runs, not an unbounded int.MaxValue.
        // This still handles unusually large student PRs while bounding batch count and cost.
        int effectiveHardMaxAdditions = allowOversizedDiff
            ? limits.ManualMaxDiffAdditions
            : limits.HardMaxDiffAdditions;
        int effectiveHardMaxFiles = allowOversizedDiff
            ? limits.ManualMaxFiles
            : limits.HardMaxFiles;
        DiffChunker.ChunkingResult chunking = DiffChunker.Prepare(
            diff,
            limits.MaxDiffAdditions,
            effectiveHardMaxAdditions,
            effectiveHardMaxFiles);

        if (chunking.ExceedsHardCap)
        {
            _logger.LogWarning(
                "Diff exceeds hard cap after filtering: {Additions} additions across {Files} files (caps {MaxAdd}/{MaxFiles}).",
                chunking.FilteredAdditions,
                chunking.FilteredFileCount,
                effectiveHardMaxAdditions,
                effectiveHardMaxFiles);
            return ReviewErrors.DiffTooLarge(
                chunking.FilteredAdditions,
                effectiveHardMaxAdditions,
                manualLimit: allowOversizedDiff);
        }

        if (chunking.Batches.Count == 0)
        {
            // Все файлы отфильтрованы (только lockfiles / generated / vendored) — кода для
            // проверки нет. НЕ LOOKS_GOOD: иначе вердикт-гейт авто-аппрувнул бы PR без
            // решения (+ XP). OFF_TOPIC → гейт вернёт студенту на доработку.
            return new ParsedAiReview(
                AiReviewVerdict.OFF_TOPIC,
                "В PR нет файлов с кодом для проверки (только сгенерированные / lock / vendored файлы). Добавьте решение задания и отправьте снова.",
                [],
                reviewer.Model,
                null,
                null);
        }

        // Single-batch fast path (большинство PR'ов) — без overhead'а агрегации.
        if (chunking.Batches.Count == 1)
        {
            // #798 — bounded-цикл дозапроса файлов применяется только здесь: на chunked
            // ревью модель и так видит лишь слайс, а need_files поверх агрегации
            // взорвал бы и стоимость, и семантику worst-verdict.
            if (repoContext is not null)
            {
                return await ReviewWithRepoContextLoopAsync(
                    spec, guidelines, pullRequest, chunking.Batches[0], reviewer, basePrompt,
                    previousReviewContext, studentReplies, repoContext, onBatchProgress, ct);
            }

            return await ReviewSingleBatchAsync(
                spec, guidelines, pullRequest, chunking.Batches[0], reviewer, basePrompt,
                previousReviewContext, studentReplies, batchIndex: 1, batchCount: 1,
                repoPrompt: null, ct);
        }

        return await ReviewBatchedAsync(
            spec, guidelines, pullRequest, chunking.Batches, reviewer, basePrompt,
            previousReviewContext, studentReplies, limits.MaxInlineComments, onBatchProgress, ct);
    }

    /// <summary>
    ///     #798 — bounded-цикл дозапроса файлов: модель либо выносит вердикт, либо
    ///     просит файлы через need_files; сервис достаёт их и повторяет вызов.
    ///     Потолки: ≤ MaxExtraRounds доп. раундов, ≤ MaxTotalFiles файлов суммарно.
    ///     Исчерпание потолка не роняет итерацию — финальный вызов идёт с инструкцией
    ///     «вердикт обязателен», непустой need_files на нём игнорируется.
    /// </summary>
    private async Task<Result<ParsedAiReview, Error>> ReviewWithRepoContextLoopAsync(
        IssueReviewSpec? spec,
        ProjectReviewGuidelines? guidelines,
        VcsPullRequest pullRequest,
        VcsDiff batch,
        EffectiveSlot reviewer,
        string basePrompt,
        string? previousReviewContext,
        string? studentReplies,
        RepoReviewContext repoContext,
        Func<CancellationToken, Task>? onRoundProgress,
        CancellationToken ct)
    {
        AssignmentReviewRepoContextOptions opts = _options.Value.RepoContext;
        List<FetchedRepoFile> fetchedFiles = [];
        List<string> requestedPaths = [];
        int extraRounds = 0;

        while (true)
        {
            int remainingBudget = Math.Max(0, opts.MaxTotalFiles - requestedPaths.Count);
            bool allowRequests = extraRounds < opts.MaxExtraRounds && remainingBudget > 0;

            RepoContextPromptData promptData = new(
                repoContext.TreeText,
                repoContext.Manifests,
                fetchedFiles,
                AllowFileRequests: allowRequests,
                MaxFilesPerRound: Math.Min(opts.MaxFilesPerRound, remainingBudget),
                RemainingFileBudget: remainingBudget,
                BudgetExhausted: !allowRequests && extraRounds > 0);

            Result<ParsedAiReview, Error> result = await ReviewSingleBatchAsync(
                spec, guidelines, pullRequest, batch, reviewer, basePrompt,
                previousReviewContext, studentReplies, batchIndex: 1, batchCount: 1,
                repoPrompt: promptData, ct);
            if (result.IsFailure)
                return result;

            ParsedAiReview parsed = result.Value;
            IReadOnlyList<string> needs = NormalizeNeedFiles(
                parsed.NeedFiles, requestedPaths, Math.Min(opts.MaxFilesPerRound, remainingBudget));

            if (!allowRequests || needs.Count == 0)
            {
                return parsed with
                {
                    NeedFiles = null,
                    RequestedFiles = requestedPaths.Count > 0 ? requestedPaths : null,
                    ContextRounds = extraRounds,
                };
            }

            _logger.LogInformation(
                "Repo-context round {Round}: model requested {Count} file(s): {Files}",
                extraRounds + 1, needs.Count, string.Join(", ", needs));

            IReadOnlyList<FetchedRepoFile> files = await repoContext.FetchFilesAsync(needs, ct);
            fetchedFiles.AddRange(files);
            requestedPaths.AddRange(needs);
            extraRounds++;

            // #690 — каждый доп. раунд это ещё один LLM-вызов (30-90с): бампаем
            // heartbeat, чтобы stale-watchdog не принял легитимный цикл за зависание.
            if (onRoundProgress is not null)
            {
                try
                {
                    await onRoundProgress(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Heartbeat after repo-context round {Round} failed (non-fatal).", extraRounds);
                }
            }
        }
    }

    /// <summary>
    ///     Санитизация need_files (#798): убираем пустое/дубли/уже запрошенное,
    ///     режем по капу за раунд. Пути нормализуем как есть (регистр/слэши — на
    ///     совести модели, несуществующий путь вернётся из fetch'а пометкой not found).
    /// </summary>
    private static IReadOnlyList<string> NormalizeNeedFiles(
        IReadOnlyList<string>? needFiles,
        IReadOnlyCollection<string> alreadyRequested,
        int maxPerRound)
    {
        if (needFiles is null || needFiles.Count == 0 || maxPerRound <= 0)
            return [];

        HashSet<string> seen = new(alreadyRequested, StringComparer.Ordinal);
        List<string> result = [];
        foreach (string raw in needFiles)
        {
            string path = raw?.Trim().TrimStart('/') ?? string.Empty;
            if (path.Length == 0 || path.Length > 500 || !seen.Add(path))
                continue;
            result.Add(path);
            if (result.Count >= maxPerRound)
                break;
        }

        return result;
    }

    private async Task<Result<ParsedAiReview, Error>> ReviewSingleBatchAsync(
        IssueReviewSpec? spec,
        ProjectReviewGuidelines? guidelines,
        VcsPullRequest pullRequest,
        VcsDiff batch,
        EffectiveSlot reviewer,
        string basePrompt,
        string? previousReviewContext,
        string? studentReplies,
        int batchIndex,
        int batchCount,
        RepoContextPromptData? repoPrompt,
        CancellationToken ct)
    {
        BuiltPrompt prompt = PromptBuilder.Build(
            spec, guidelines, pullRequest, batch, basePrompt, previousReviewContext,
            studentReplies, batchIndex, batchCount, repoPrompt);

        // #798 — need_files в схеме только когда repo-контекст активен; иначе схема
        // байт-в-байт прежняя (flag off = старое поведение).
        bool includeNeedFiles = repoPrompt is not null;

        AiGenerationRequest request = new()
        {
            Model = reviewer.Model,
            SystemPrompt = prompt.SystemPrompt,
            UserPrompt = prompt.UserPrompt,
            Temperature = reviewer.Temperature,
            MaxOutputTokens = reviewer.MaxOutputTokens,
            TimeoutSeconds = reviewer.TimeoutSeconds,
            OutputMode = AiOutputMode.JsonSchema,
            JsonSchema = AiReviewResponseSchema.Build(includeNeedFiles),
        };

        // Первый запрос. Если parse fail — один retry с явным "JSON ONLY" hint'ом.
        Result<AiGenerationResult<JsonElement>, Error> firstCall =
            await _aiClient.GenerateAsync<JsonElement>(request, ct);

        // Reasoning-модели (deepseek-v4-pro) интермиттентно отдают пустой ответ, когда
        // reasoning съедает весь output-бюджет (ai.output.empty). Это транзиентно —
        // повторный вызов почти всегда проходит. Ретраим РОВНО пустой ответ один раз;
        // прочие сбои (timeout/unauthorized/rate-limited) Polly уже отретраил на
        // транспортном уровне, дёргать LLM повторно смысла нет.
        if (firstCall.IsFailure
            && string.Equals(firstCall.Error.Messages[0].Code, "ai.output.empty", StringComparison.Ordinal))
        {
            _logger.LogWarning("LLM returned empty output, retrying once before failing the iteration.");
            firstCall = await _aiClient.GenerateAsync<JsonElement>(request, ct);
        }

        // `ai.output.invalid` — это НЕ недоступность провайдера: модель ответила, но JSON
        // усечён/битый или не прошёл строгую схему (частая болячка reasoning-модели —
        // reasoning съел output-бюджет и обрубил JSON посреди). Такое лечится reformat-retry
        // ниже (temp 0 + «VALID JSON ONLY»), а не «попробуй позже». Прочие failure-коды
        // (timeout / unauthorized / rate-limited / provider.failed / context.exceeded /
        // empty-после-ретрая) — реальная недоступность/неисполнимость: Polly уже отретраил
        // транспорт, отдаём LlmUnavailable, RunIteration отретраит на своём уровне.
        bool firstCallInvalidOutput = firstCall.IsFailure
            && string.Equals(firstCall.Error.Messages[0].Code, "ai.output.invalid", StringComparison.Ordinal);

        if (firstCall.IsFailure && !firstCallInvalidOutput)
        {
            _logger.LogWarning("LLM call failed: {Code} {Message}",
                firstCall.Error.Messages[0].Code,
                firstCall.Error.Messages[0].Message);
            return ReviewErrors.LlmUnavailable(firstCall.Error.Messages[0].Message);
        }

        // firstCall либо успех (→ ParseAndProject), либо ai.output.invalid (→ прокидываем как
        // parse-failure, чтобы сработал общий reformat-retry ниже). parsed.IsSuccess отсюда
        // всегда влечёт firstCall.IsSuccess (mojibake-блок ниже безопасен).
        Result<ParsedAiReview, Error> parsed = firstCall.IsSuccess
            ? ParseAndProject(firstCall.Value)
            : Result.Failure<ParsedAiReview, Error>(firstCall.Error);

        // Провайдер интермиттентно отдаёт кириллицу как Latin-1 mojibake, местами с
        // выеденными байтами — такое репарация восстанавливает лишь частично (#475).
        // Свежая генерация почти всегда чистая → при артефактах после репарации ретраим
        // один раз; если retry тоже битый/упал — отдаём best-effort первого ответа.
        if (parsed.IsSuccess && HasEncodingArtifacts(parsed.Value))
        {
            _logger.LogWarning(
                "AI review text arrived mojibake-corrupted in batch {BatchIndex}/{BatchCount} and could not be fully repaired — retrying once for a clean generation.",
                batchIndex,
                batchCount);

            Result<AiGenerationResult<JsonElement>, Error> cleanCall =
                await _aiClient.GenerateAsync<JsonElement>(request, ct);
            if (cleanCall.IsSuccess)
            {
                Result<ParsedAiReview, Error> cleanParsed = ParseAndProject(cleanCall.Value);
                if (cleanParsed.IsSuccess && !HasEncodingArtifacts(cleanParsed.Value))
                    return cleanParsed;
            }

            return parsed;
        }

        if (parsed.IsSuccess)
            return parsed;

        _logger.LogWarning(
            "AI returned invalid output, attempting one retry: {Detail}",
            parsed.Error.Messages[0].Message);

        AiGenerationRequest retry = new()
        {
            Model = request.Model,
            SystemPrompt = prompt.SystemPrompt + "\n\nReply with VALID JSON ONLY matching the schema.",
            UserPrompt = prompt.UserPrompt,
            Temperature = 0,
            MaxOutputTokens = request.MaxOutputTokens,
            TimeoutSeconds = request.TimeoutSeconds,
            OutputMode = AiOutputMode.JsonSchema,
            JsonSchema = AiReviewResponseSchema.Build(includeNeedFiles),
        };

        Result<AiGenerationResult<JsonElement>, Error> secondCall =
            await _aiClient.GenerateAsync<JsonElement>(retry, ct);
        if (secondCall.IsFailure)
        {
            // Повторно битый вывод (ai.output.invalid) — это качество ответа модели, а не
            // «провайдер лёг»: классифицируем как invalid_output (UI → «AI вернул некорректный
            // ответ, попробуй ещё раз» + «Перепроверить»), не гоняя RunReviewerWithRetryAsync
            // ещё 3× по провайдеру. Настоящая недоступность на retry остаётся unavailable.
            return string.Equals(secondCall.Error.Messages[0].Code, "ai.output.invalid", StringComparison.Ordinal)
                ? ReviewErrors.LlmInvalidOutput(secondCall.Error.Messages[0].Message)
                : ReviewErrors.LlmUnavailable(secondCall.Error.Messages[0].Message);
        }

        Result<ParsedAiReview, Error> reparsed = ParseAndProject(secondCall.Value);
        return reparsed.IsSuccess
            ? reparsed
            : ReviewErrors.LlmInvalidOutput(reparsed.Error.Messages[0].Message);
    }

    /// <summary>
    ///     Multi-batch path (#18): ревьюит каждый batch отдельно, агрегирует
    ///     результат. Verdict = worst across batches; inline comments merged +
    ///     capped; summary — одна короткая комбинированная выжимка.
    /// </summary>
    private async Task<Result<ParsedAiReview, Error>> ReviewBatchedAsync(
        IssueReviewSpec? spec,
        ProjectReviewGuidelines? guidelines,
        VcsPullRequest pullRequest,
        IReadOnlyList<VcsDiff> batches,
        EffectiveSlot reviewer,
        string basePrompt,
        string? previousReviewContext,
        string? studentReplies,
        int maxInlineComments,
        Func<CancellationToken, Task>? onBatchProgress,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Large diff — splitting into {BatchCount} batches for review.", batches.Count);

        List<ParsedAiReview> results = [];
        for (int i = 0; i < batches.Count; i++)
        {
            Result<ParsedAiReview, Error> batchResult = await ReviewSingleBatchAsync(
                spec, guidelines, pullRequest, batches[i], reviewer, basePrompt,
                previousReviewContext, studentReplies, batchIndex: i + 1, batchCount: batches.Count,
                repoPrompt: null, ct);

            // Любой batch упал (LLM unavailable / invalid output) → fail всю
            // iteration. Частичный результат не публикуем — это сбивает с толку.
            // Heartbeat на failure не нужен: iteration здесь же завершается (persist FAILED).
            if (batchResult.IsFailure)
                return batchResult.Error;

            results.Add(batchResult.Value);

            // #690 — liveness-heartbeat после каждого batch'а: длинное multi-batch
            // ревью прогрессирует, и watchdog не должен принять его за зависшее и
            // переотправить (что starve'ило бы его — каждый прогон killed lease-check'ом
            // до завершения). Best-effort: heartbeat-сбой не валит ревью.
            if (onBatchProgress is not null)
            {
                try
                {
                    await onBatchProgress(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        ex, "Heartbeat after batch {BatchIndex}/{BatchCount} failed (non-fatal).",
                        i + 1, batches.Count);
                }
            }
        }

        return Aggregate(results, maxInlineComments);
    }

    /// <summary>
    ///     Агрегирует per-batch результаты в один <see cref="ParsedAiReview"/>:
    ///     verdict = worst (OFF_TOPIC > MAJOR > MINOR > LOOKS_GOOD), inline comments
    ///     merged + capped, summary — комбинированная выжимка ≤400 символов,
    ///     token usage суммируется.
    /// </summary>
    private static Result<ParsedAiReview, Error> Aggregate(
        IReadOnlyList<ParsedAiReview> results, int maxInlineComments)
    {
        AiReviewVerdict worstVerdict = results
            .Select(r => r.Verdict)
            .Aggregate(AiReviewVerdict.LOOKS_GOOD, WorstOf);

        List<VcsReviewComment> mergedComments = results
            .SelectMany(r => r.InlineComments)
            .Take(maxInlineComments)
            .ToList();

        string combinedSummary = BuildCombinedSummary(results);

        string model = results[0].ModelUsed;
        int? inputTokens = SumNullable(results.Select(r => r.InputTokens));
        int? outputTokens = SumNullable(results.Select(r => r.OutputTokens));

        return new ParsedAiReview(
            worstVerdict,
            combinedSummary,
            mergedComments,
            model,
            inputTokens,
            outputTokens);
    }

    private static string BuildCombinedSummary(IReadOnlyList<ParsedAiReview> results)
    {
        // Берём непустые per-batch summary, склеиваем, тримим до 400 символов
        // (тот же контракт, что у single-batch summary).
        string joined = string.Join(
            " ",
            results
                .Select(r => r.Summary.Trim())
                .Where(s => !string.IsNullOrEmpty(s)));

        if (joined.Length <= 400)
            return joined;
        return joined[..397].TrimEnd() + "...";
    }

    private static int? SumNullable(IEnumerable<int?> values)
    {
        int sum = 0;
        bool any = false;
        foreach (int? v in values)
        {
            if (v is { } value)
            {
                sum += value;
                any = true;
            }
        }

        return any ? sum : null;
    }

    /// <summary>
    ///     Precedence для агрегации (#18): OFF_TOPIC > MAJOR_ISSUES > MINOR_ISSUES >
    ///     LOOKS_GOOD. Возвращает наиболее серьёзный из двух verdict'ов.
    /// </summary>
    private static AiReviewVerdict WorstOf(AiReviewVerdict a, AiReviewVerdict b) =>
        Severity(a) >= Severity(b) ? a : b;

    private static int Severity(AiReviewVerdict verdict) => verdict switch
    {
        AiReviewVerdict.OFF_TOPIC => 3,
        AiReviewVerdict.MAJOR_ISSUES => 2,
        AiReviewVerdict.MINOR_ISSUES => 1,
        _ => 0,
    };

    private static bool HasEncodingArtifacts(ParsedAiReview review) =>
        AiReviewTextEncodingRepair.ContainsMojibakeArtifacts(review.Summary)
        || review.InlineComments.Any(c =>
            AiReviewTextEncodingRepair.ContainsMojibakeArtifacts(c.Body)
            || (c.Suggestion is not null
                && AiReviewTextEncodingRepair.ContainsMojibakeArtifacts(c.Suggestion)));

    private Result<ParsedAiReview, Error> ParseAndProject(
        AiGenerationResult<JsonElement> result)
    {
        AiReviewResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AiReviewResponseDto>(
                result.Value.GetRawText(),
                JSON_OPTIONS);
        }
        catch (JsonException ex)
        {
            return ReviewErrors.LlmInvalidOutput($"json parse error: {ex.Message}");
        }

        if (dto is null)
            return ReviewErrors.LlmInvalidOutput("payload is null");

        if (!Enum.TryParse(dto.Verdict, ignoreCase: false, out AiReviewVerdict verdict))
            return ReviewErrors.LlmInvalidOutput($"unknown verdict '{dto.Verdict}'");

        if (string.IsNullOrWhiteSpace(dto.Summary))
            return ReviewErrors.LlmInvalidOutput("summary missing or empty");

        IReadOnlyList<AiReviewInlineCommentDto> comments = dto.InlineComments ?? [];
        int maxComments = _options.Value.Limits.MaxInlineComments;
        if (comments.Count > maxComments)
        {
            _logger.LogDebug(
                "AI returned {Count} comments — truncating to MaxInlineComments={Max}.",
                comments.Count,
                maxComments);
            comments = [.. comments.Take(maxComments)];
        }

        List<VcsReviewComment> inlineComments = comments
            .Where(c => !string.IsNullOrEmpty(c.Path) && c.Line > 0 && !string.IsNullOrEmpty(c.Body))
            .Select(c => new VcsReviewComment(
                c.Path,
                c.Line,
                AiReviewTextEncodingRepair.RepairUtf8Mojibake(c.Body),
                c.Suggestion is null
                    ? null
                    : AiReviewTextEncodingRepair.RepairUtf8Mojibake(c.Suggestion)))
            .ToList();

        return new ParsedAiReview(
            verdict,
            AiReviewTextEncodingRepair.RepairUtf8Mojibake(dto.Summary.Trim()),
            inlineComments,
            result.Model,
            result.Usage?.InputTokens,
            result.Usage?.OutputTokens,
            NeedFiles: dto.NeedFiles);
    }
}
