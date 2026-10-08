using SharedKernel;
using Shared.AI;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.Domain;

namespace TrainerService.IntegrationTests.Infrastructure;

/// <summary>
///     Test fake for <see cref="IOpenAnswerGrader"/> — no real LLM. Deterministic:
///     <list type="bullet">
///         <item>empty/whitespace answer → INCORRECT/0 + "Ответа нет" (mirrors production);</item>
///         <item>student text containing <see cref="ThrowSentinel"/> → throws (exercise the inline fallback);</item>
///         <item><see cref="FailNext"/> set → returns a failure Result (exercise the grader-returned-failure fallback);</item>
///         <item>otherwise → the configured <see cref="Verdict"/>/<see cref="ScorePercent"/>/<see cref="Feedback"/>
///             (default CORRECT/100 for any non-empty text).</item>
///     </list>
///     Re-created per test in <c>ResetDatabaseAsync</c> so configured grades don't leak.
/// </summary>
public sealed class FakeOpenAnswerGrader : IOpenAnswerGrader
{
    /// <summary>Student text that, when present, makes the grader throw — exercises the fallback path.</summary>
    public const string ThrowSentinel = "__throw__";

    public AnswerVerdict Verdict { get; set; } = AnswerVerdict.CORRECT;

    public int ScorePercent { get; set; } = 100;

    public string? Feedback { get; set; } = "Хороший ответ.";

    /// <summary>
    ///     AI usage attached to a successful grade (#614 C1 ledger). Default carries known tokens so a
    ///     ledger-cost assertion is deterministic. Set null to simulate a provider that returned no usage.
    /// </summary>
    public AiUsage? Usage { get; set; } = new AiUsage(InputTokens: 200, OutputTokens: 50, TotalTokens: 250);

    /// <summary>Model reported by the grade — non-empty means «a real LLM call happened» to the caller's ledger.</summary>
    public string Model { get; set; } = "gpt-4.1-mini";

    /// <summary>When true, the next (and all subsequent) grade calls return a failure Result.</summary>
    public bool FailNext { get; set; }

    public int CallCount { get; private set; }

    public Task<Result<OpenAnswerGrade, Error>> GradeAsync(
        string questionStem,
        string? referenceAnswer,
        string? studentText,
        CancellationToken ct)
    {
        CallCount++;

        if (string.IsNullOrWhiteSpace(studentText))
        {
            // Empty answer → INCORRECT/0 without an LLM call: no usage, empty model (mirrors production).
            return Task.FromResult(
                Result.Success<OpenAnswerGrade, Error>(new OpenAnswerGrade(AnswerVerdict.INCORRECT, 0, "Ответа нет")));
        }

        if (studentText.Contains(ThrowSentinel, StringComparison.Ordinal))
            throw new InvalidOperationException("fake grader sentinel throw");

        if (FailNext)
        {
            return Task.FromResult(
                Result.Failure<OpenAnswerGrade, Error>(
                    Error.Failure("test.ai.unavailable", "AI недоступен (тест).").AsTransient()));
        }

        return Task.FromResult(
            Result.Success<OpenAnswerGrade, Error>(new OpenAnswerGrade(Verdict, ScorePercent, Feedback, Usage, Model)));
    }
}
