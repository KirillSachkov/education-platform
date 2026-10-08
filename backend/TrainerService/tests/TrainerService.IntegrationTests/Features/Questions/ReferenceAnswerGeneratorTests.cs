using System.Text.Json;
using Microsoft.Extensions.Options;
using Shared.AI;
using TrainerService.Core.Configuration;
using TrainerService.Core.Features.Sessions.Grading;

namespace TrainerService.IntegrationTests.Features.Questions;

/// <summary>
///     Unit tests for <see cref="ReferenceAnswerGenerator"/> (#691 t6) with a mocked <see cref="IAiClient"/> —
///     no DB, no HTTP. Covers: happy path (model answer parsed out of the JSON-schema payload),
///     LLM-failure propagation, and the empty-stem guard that skips the LLM call entirely.
/// </summary>
public sealed class ReferenceAnswerGeneratorTests
{
    private readonly IAiClient _aiClient = Substitute.For<IAiClient>();

    private ReferenceAnswerGenerator CreateGenerator() =>
        new(_aiClient, Options.Create(new TrainerAiOptions()));

    [Fact]
    public async Task GenerateAsync_returns_model_answer_from_llm()
    {
        const string answer = "Сборщик мусора (GC) освобождает память поколениями 0/1/2; выжившие продвигаются.";
        StubReference(answer);
        ReferenceAnswerGenerator generator = CreateGenerator();

        Result<string, Error> result = await generator.GenerateAsync(
            "Как работает поколенческий GC?", explanation: "Про поколения.", section: "Память", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(answer, result.Value);

        // Calls the LLM with the trainer_reference_answer JSON schema (structured output).
        await _aiClient.Received(1).GenerateAsync<JsonElement>(
            Arg.Is<AiGenerationRequest>(r =>
                r.JsonSchema != null
                && string.Equals(r.JsonSchema.Name, ReferenceAnswerGenerator.REFERENCE_ANSWER_SCHEMA_NAME, StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_propagates_llm_failure()
    {
        _aiClient.GenerateAsync<JsonElement>(Arg.Any<AiGenerationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AiGenerationResult<JsonElement>, Error>(
                Error.Failure("ai.down", "Провайдер недоступен.")));
        ReferenceAnswerGenerator generator = CreateGenerator();

        Result<string, Error> result = await generator.GenerateAsync("Вопрос?", null, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ai.down", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task GenerateAsync_empty_stem_fails_without_calling_llm()
    {
        ReferenceAnswerGenerator generator = CreateGenerator();

        Result<string, Error> result = await generator.GenerateAsync("   ", null, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("trainer.question.stem.required", result.Error.Messages[0].Code);
        await _aiClient.DidNotReceive().GenerateAsync<JsonElement>(
            Arg.Any<AiGenerationRequest>(), Arg.Any<CancellationToken>());
    }

    private void StubReference(string answer)
    {
        string json = JsonSerializer.Serialize(new { referenceAnswer = answer });
        JsonElement element = JsonDocument.Parse(json).RootElement.Clone();
        _aiClient.GenerateAsync<JsonElement>(Arg.Any<AiGenerationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<AiGenerationResult<JsonElement>, Error>(
                new AiGenerationResult<JsonElement>(element, "test", "gpt-4.1-mini", null, AiFinishReason.Stop)));
    }
}
