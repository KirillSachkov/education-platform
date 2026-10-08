using System.Text.Json;
using Core.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.AI;
using TrainerService.Core.Configuration;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Sessions;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.Grading;

/// <summary>
///     AI-grades the open (OPEN_TEXT) answers of a completed mock-interview session (#585), then
///     recomputes the final score and writes an overall interview feedback. Runs out-of-band in a
///     background scope (after Complete), never on the student's request thread. Auto-gradable items
///     are already scored deterministically (CheckAnswer); this only fills in the open answers.
///     <para>
///         The per-answer grading reuses <see cref="IOpenAnswerGrader"/> (shared with the inline
///         non-mock path in <c>CheckAnswerHandler</c>); this service only adds the aggregate
///         interview-level feedback on top.
///     </para>
/// </summary>
public sealed class MockAnswerGradingService
{
    private const string MOCK_FEEDBACK_SCHEMA_NAME = "trainer_mock_feedback";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITrainingSessionsRepository _sessions;
    private readonly IOpenAnswerGrader _openAnswerGrader;
    private readonly IAiClient _aiClient;
    private readonly AiUsageLedger _aiUsageLedger;
    private readonly IOptions<TrainerAiOptions> _options;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<MockAnswerGradingService> _logger;

    public MockAnswerGradingService(
        ITrainingSessionsRepository sessions,
        IOpenAnswerGrader openAnswerGrader,
        IAiClient aiClient,
        AiUsageLedger aiUsageLedger,
        IOptions<TrainerAiOptions> options,
        ITransactionManager transactions,
        ILogger<MockAnswerGradingService> logger)
    {
        _sessions = sessions;
        _openAnswerGrader = openAnswerGrader;
        _aiClient = aiClient;
        _aiUsageLedger = aiUsageLedger;
        _options = options;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task GradeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        Result<TrainingSession, Error> sessionResult =
            await _sessions.GetWithItemsAsync(s => s.Id == sessionId, ct);
        if (sessionResult.IsFailure)
            return;

        TrainingSession session = sessionResult.Value;

        // Idempotent: a re-enqueue (startup recovery / double-complete) of an already-graded
        // session is a no-op. GRADING is allowed to re-run (a crash mid-grade left it stuck).
        if (session.GradingStatus == GradingStatus.GRADED)
            return;

        try
        {
            session.MarkGrading();
            UnitResult<Error> markResult = await _transactions.SaveChangesAsync(ct);
            if (markResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to mark session {SessionId} as GRADING: {Code}",
                    sessionId, markResult.Error.Messages[0].Code);
                return;
            }

            TrainerGradingOptions grading = _options.Value.Grading;

            // Collect the AI usage of every LLM call in this grading pass (#614 C1). Recorded in the
            // ledger AFTER the session is persisted — the ledger does its own SaveChanges, so writing
            // it mid-pass would prematurely flush partial session state.
            List<(AiUsageOperation Operation, string Model, AiUsage? Usage)> usages = [];

            foreach (TrainingSessionItem item in session.Items)
            {
                if (!string.Equals(item.QuestionType, AnswerGrader.OPEN_TEXT, StringComparison.Ordinal)
                    || item.AnsweredAt is null
                    || item.ScorePercent is not null)
                {
                    continue;
                }

                await GradeOpenItemAsync(session, item, usages, ct);
            }

            // Reuse the completion formula after deferred grades arrive: every deterministic item
            // stays in the denominator (unanswered = 0), scored open answers participate, and
            // unanswered/failed open answers remain excluded. A second formula here previously made
            // the score jump upward after AI grading by dropping skipped deterministic questions.
            session.SetFinalScore(SessionScoreCalculator.ComputeAtCompletion(session.Items));

            bool hasUngradedOpenAnswers = session.Items.Any(i =>
                string.Equals(i.QuestionType, AnswerGrader.OPEN_TEXT, StringComparison.Ordinal)
                && i.AnsweredAt is not null
                && i.ScorePercent is null);

            if (hasUngradedOpenAnswers)
            {
                // Do not publish a false terminal GRADED state when one or more per-item AI calls
                // failed. FAILED is the existing UI contract for showing the deterministic score
                // without claiming that a complete AI review exists.
                session.MarkGradingFailed();
            }
            else
            {
                await GradeOverallAsync(session, grading, usages, ct);
                session.MarkGraded();
            }

            UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
            if (saveResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to persist graded session {SessionId}: {Code}",
                    sessionId, saveResult.Error.Messages[0].Code);
            }

            // Record the AI-usage ledger rows for this session (per-item open grades + the aggregate
            // feedback call). Best-effort — a ledger error never affects the already-graded session.
            foreach ((AiUsageOperation operation, string model, AiUsage? usage) in usages)
            {
                if (!string.IsNullOrEmpty(model))
                    await _aiUsageLedger.RecordLlmAsync(session.UserId, operation, model, usage, session.Id, ct);
            }
        }
#pragma warning disable CA1031 // grading must degrade gracefully — never crash the background loop
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Mock AI grading threw for session {SessionId}; marking FAILED.", sessionId);
            session.MarkGradingFailed();
            // Best-effort persist of the FAILED status; swallow a secondary failure.
            UnitResult<Error> failSave = await _transactions.SaveChangesAsync(ct);
            if (failSave.IsFailure)
                _logger.LogError("Failed to persist FAILED status for session {SessionId}.", sessionId);
        }
    }

    /// <summary>
    ///     Grades one open answer via the shared <see cref="IOpenAnswerGrader"/>. A per-item failure
    ///     (AI down / invalid output) leaves the item PENDING (excluded from the score average); after
    ///     the pass the session becomes FAILED instead of falsely reporting a complete GRADED review.
    /// </summary>
    private async Task GradeOpenItemAsync(
        TrainingSession session,
        TrainingSessionItem item,
        List<(AiUsageOperation Operation, string Model, AiUsage? Usage)> usages,
        CancellationToken ct)
    {
        try
        {
            GradingKey key = DeserializeKey(item.GradingKeyJson);

            Result<OpenAnswerGrade, Error> grade = await _openAnswerGrader.GradeAsync(
                item.QuestionText, key.ReferenceAnswer, item.AnswerRaw, ct);
            if (grade.IsFailure)
            {
                _logger.LogWarning(
                    "Open-answer grading failed for item {ItemId} in session {SessionId}: {Code} — leaving PENDING.",
                    item.Id, session.Id, grade.Error.Messages[0].Code);
                return;
            }

            // Capture the per-item grade's AI usage for the ledger (#614 C1). Empty-answer grades skip
            // the LLM → model is empty → not recorded (filtered when flushing).
            usages.Add((AiUsageOperation.OPEN_ANSWER_GRADE, grade.Value.Model, grade.Value.Usage));

            session.ApplyAiGrade(item.Id, grade.Value.Verdict, grade.Value.ScorePercent, grade.Value.Feedback);
        }
#pragma warning disable CA1031 // one bad item must not fail the whole session — leave it PENDING
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(
                ex, "Open-answer grading threw for item {ItemId} in session {SessionId} — leaving PENDING.",
                item.Id, session.Id);
        }
    }

    /// <summary>
    ///     One aggregate LLM call producing the overall interview feedback (weak topics / strengths /
    ///     summary) over the full answered set. A failure leaves the overall fields null (the per-item
    ///     verdicts still stand).
    /// </summary>
    private async Task GradeOverallAsync(
        TrainingSession session,
        TrainerGradingOptions grading,
        List<(AiUsageOperation Operation, string Model, AiUsage? Usage)> usages,
        CancellationToken ct)
    {
        try
        {
            var lines = session.Items
                .OrderBy(i => i.SortIndex)
                .Select(i =>
                {
                    string verdict = i.Verdict?.ToString() ?? AnswerVerdict.PENDING.ToString();
                    string score = i.ScorePercent is int s ? $"{s}%" : "—";
                    string topic = string.IsNullOrWhiteSpace(i.Section) ? "" : $" [{i.Section}]";
                    return $"- {i.QuestionText}{topic}: {verdict} ({score})";
                });
            string answersBlock = string.Join("\n", lines);

            AiGenerationRequest request = new()
            {
                Model = grading.Model,
                Temperature = grading.Temperature,
                MaxOutputTokens = grading.MaxOutputTokens,
                TimeoutSeconds = grading.TimeoutSeconds,
                OutputMode = AiOutputMode.JsonSchema,
                JsonSchema = BuildMockFeedbackSchema(),
                SystemPrompt =
                    "Ты — наставник, дающий умный фидбэк по итогам пробного собеседования. На основе " +
                    "списка вопросов и результатов (вердикт + балл по каждому) выдели: какие темы слабые " +
                    "(weakTopics), чего не хватает и над чем поработать, и что получилось хорошо (strengths). " +
                    "overallFeedback — связный абзац на русском (2-4 предложения), доброжелательно и по делу. " +
                    "weakTopics и strengths — короткие пункты (по 0-5 штук, можно пусто).",
                UserPrompt = $"Результаты собеседования:\n{answersBlock}",
            };

            Result<MockFeedback, Error> feedback = await GenerateMockFeedbackAsync(request, ct);
            if (feedback.IsFailure)
            {
                _logger.LogWarning(
                    "Overall mock feedback failed for session {SessionId}: {Code} — leaving overall null.",
                    session.Id, feedback.Error.Messages[0].Code);
                return;
            }

            // Capture the aggregate-feedback LLM call's usage for the ledger (#614 C1).
            usages.Add((AiUsageOperation.MOCK_AGGREGATE, feedback.Value.Model, feedback.Value.Usage));

            session.RecordAiOverall(
                feedback.Value.OverallFeedback,
                JsonSerializer.Serialize(feedback.Value.WeakTopics ?? [], JSON_OPTIONS),
                JsonSerializer.Serialize(feedback.Value.Strengths ?? [], JSON_OPTIONS));
        }
#pragma warning disable CA1031 // overall feedback is non-critical — never fail the session for it
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Overall mock feedback threw for session {SessionId} — leaving overall null.", session.Id);
        }
    }

    private async Task<Result<MockFeedback, Error>> GenerateMockFeedbackAsync(
        AiGenerationRequest request, CancellationToken ct)
    {
        Result<AiGenerationResult<JsonElement>, Error> call = await _aiClient.GenerateAsync<JsonElement>(request, ct);
        AiUsage? totalUsage = call.IsSuccess ? call.Value.Usage : null;

        if (ShouldRetry(call))
        {
            call = await _aiClient.GenerateAsync<JsonElement>(request, ct);
            if (call.IsSuccess)
                totalUsage = AddUsage(totalUsage, call.Value.Usage);
        }

        if (call.IsFailure)
            return call.Error;

        AiGenerationResult<JsonElement> billable = call.Value;
        MockFeedbackDto? dto = TryDeserialize<MockFeedbackDto>(call.Value.Value);
        if (dto is null)
        {
            Result<AiGenerationResult<JsonElement>, Error> retry = await _aiClient.GenerateAsync<JsonElement>(request, ct);
            if (retry.IsFailure)
                return retry.Error;
            totalUsage = AddUsage(totalUsage, retry.Value.Usage);
            billable = retry.Value;
            dto = TryDeserialize<MockFeedbackDto>(retry.Value.Value);
            if (dto is null)
                return TrainerServiceErrors.Transcribe.Failed();
        }

        return new MockFeedback(
            NormalizeFeedback(dto.OverallFeedback), dto.WeakTopics, dto.Strengths, totalUsage, billable.Model);
    }

    private static AiUsage? AddUsage(AiUsage? total, AiUsage? next)
    {
        if (next is null)
            return total;
        if (total is null)
            return next;

        return new AiUsage(
            total.InputTokens + next.InputTokens,
            total.OutputTokens + next.OutputTokens,
            total.TotalTokens + next.TotalTokens);
    }

    private static bool ShouldRetry(Result<AiGenerationResult<JsonElement>, Error> call) =>
        call.IsFailure
        && string.Equals(call.Error.Messages[0].Code, "ai.output.empty", StringComparison.Ordinal);

    private static T? TryDeserialize<T>(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), JSON_OPTIONS);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static GradingKey DeserializeKey(string? gradingKeyJson)
    {
        if (string.IsNullOrEmpty(gradingKeyJson))
            return new GradingKey([], null, null);

        return JsonSerializer.Deserialize<GradingKey>(gradingKeyJson, SessionMapper.JsonOptions)
            ?? new GradingKey([], null, null);
    }

    private static string? NormalizeFeedback(string? feedback) =>
        string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim();

    private static AiJsonSchema BuildMockFeedbackSchema() =>
        new(
            MOCK_FEEDBACK_SCHEMA_NAME,
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "overallFeedback": { "type": "string" },
                "weakTopics": { "type": "array", "items": { "type": "string" } },
                "strengths": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["overallFeedback", "weakTopics", "strengths"]
            }
            """,
            "Итоговый фидбэк по результатам пробного собеседования.");

    private readonly record struct MockFeedback(
        string? OverallFeedback,
        IReadOnlyList<string>? WeakTopics,
        IReadOnlyList<string>? Strengths,
        AiUsage? Usage,
        string Model);

    private sealed record MockFeedbackDto(
        string? OverallFeedback,
        IReadOnlyList<string>? WeakTopics,
        IReadOnlyList<string>? Strengths);
}
