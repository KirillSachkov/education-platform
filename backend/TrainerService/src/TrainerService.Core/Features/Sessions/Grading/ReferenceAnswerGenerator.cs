using System.Text.Json;
using Microsoft.Extensions.Options;
using Shared.AI;
using TrainerService.Core.Configuration;
using TrainerService.Domain;

namespace TrainerService.Core.Features.Sessions.Grading;

/// <summary>
///     Генерирует эталонный (образцовый) ответ для OPEN_TEXT-вопроса (#691 t6). Зеркалит стиль
///     <see cref="OpenAnswerGrader"/>: AI-клиент + промпт + JSON-схема <c>trainer_reference_answer</c>
///     живут здесь, чтобы admin-backfill (<c>BackfillReferenceAnswersHandler</c>) мог получить черновой
///     эталон, который владелец позже отредактирует. Эталон нужен грейдеру открытых ответов как ориентир
///     и фронту для блока «Эталонный ответ».
///     <para>Грейдер/генератор остаётся context-free (не знает про userId/sessionId). На любом сбое/таймауте
///     возвращает <c>Result.Failure</c> — НЕ кидает исключение; caller сам решает, как деградировать.</para>
/// </summary>
public interface IReferenceAnswerGenerator
{
    /// <summary>
    ///     Строит лаконичный эталонный ответ на русском по тексту вопроса (+ опц. раздел/пояснение).
    ///     Пустой стем → <c>Result.Failure</c> без вызова LLM.
    /// </summary>
    Task<Result<string, Error>> GenerateAsync(
        string questionStem,
        string? explanation,
        string? section,
        CancellationToken ct);
}

/// <inheritdoc cref="IReferenceAnswerGenerator"/>
public sealed class ReferenceAnswerGenerator : IReferenceAnswerGenerator
{
    public const string REFERENCE_ANSWER_SCHEMA_NAME = "trainer_reference_answer";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAiClient _aiClient;
    private readonly IOptions<TrainerAiOptions> _options;

    public ReferenceAnswerGenerator(IAiClient aiClient, IOptions<TrainerAiOptions> options)
    {
        _aiClient = aiClient;
        _options = options;
    }

    public async Task<Result<string, Error>> GenerateAsync(
        string questionStem,
        string? explanation,
        string? section,
        CancellationToken ct)
    {
        // Defensive: a question always has a stem (domain-required), but guard before spending an LLM call.
        if (string.IsNullOrWhiteSpace(questionStem))
            return TrainerServiceErrors.Question.StemRequired();

        TrainerGradingOptions grading = _options.Value.Grading;

        string userPrompt = $"Вопрос: {questionStem}";
        if (!string.IsNullOrWhiteSpace(section))
            userPrompt += $"\n\nРаздел: {section}";
        if (!string.IsNullOrWhiteSpace(explanation))
            userPrompt += $"\n\nДополнительный контекст (разбор автора): {explanation}";

        AiGenerationRequest request = new()
        {
            Model = grading.Model,
            Temperature = grading.Temperature,
            MaxOutputTokens = grading.MaxOutputTokens,
            TimeoutSeconds = grading.TimeoutSeconds,
            OutputMode = AiOutputMode.JsonSchema,
            JsonSchema = BuildReferenceSchema(),
            SystemPrompt =
                "Ты — опытный IT-интервьюер и преподаватель. Напиши ЭТАЛОННЫЙ (образцовый) ответ на " +
                "вопрос собеседования — такой, с которым потом будут сравнивать ответы кандидатов. " +
                "Требования к ответу: по-русски; точный и по существу; раскрывает суть, но без воды; " +
                "2–5 предложений (для простого вопроса — короче); БЕЗ вступлений вроде «Этот вопрос о…» " +
                "и БЕЗ обращений к читателю — только сам ответ. Не используй markdown-заголовки.",
            UserPrompt = userPrompt,
        };

        return await GenerateReferenceAsync(request, ct);
    }

    /// <summary>Вызывает LLM за одним эталоном; повторяет один раз на пустом/невалидном выводе (как <see cref="OpenAnswerGrader"/>).</summary>
    private async Task<Result<string, Error>> GenerateReferenceAsync(AiGenerationRequest request, CancellationToken ct)
    {
        Result<AiGenerationResult<JsonElement>, Error> call = await _aiClient.GenerateAsync<JsonElement>(request, ct);

        if (ShouldRetry(call))
            call = await _aiClient.GenerateAsync<JsonElement>(request, ct);

        if (call.IsFailure)
            return call.Error;

        string? answer = TryReadReference(call.Value.Value);
        if (string.IsNullOrWhiteSpace(answer))
        {
            // One retry on invalid/empty JSON payload.
            Result<AiGenerationResult<JsonElement>, Error> retry = await _aiClient.GenerateAsync<JsonElement>(request, ct);
            if (retry.IsFailure)
                return retry.Error;
            answer = TryReadReference(retry.Value.Value);
            if (string.IsNullOrWhiteSpace(answer))
                return TrainerServiceErrors.Transcribe.Failed();
        }

        return answer.Trim();
    }

    private static bool ShouldRetry(Result<AiGenerationResult<JsonElement>, Error> call) =>
        call.IsFailure
        && string.Equals(call.Error.Messages[0].Code, "ai.output.empty", StringComparison.Ordinal);

    private static string? TryReadReference(JsonElement element)
    {
        try
        {
            ReferenceAnswerDto? dto = JsonSerializer.Deserialize<ReferenceAnswerDto>(element.GetRawText(), JSON_OPTIONS);
            return dto?.ReferenceAnswer;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AiJsonSchema BuildReferenceSchema() =>
        new(
            REFERENCE_ANSWER_SCHEMA_NAME,
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "referenceAnswer": { "type": "string" }
              },
              "required": ["referenceAnswer"]
            }
            """,
            "Эталонный (образцовый) ответ на открытый вопрос собеседования.");

    private sealed record ReferenceAnswerDto(string? ReferenceAnswer);
}
