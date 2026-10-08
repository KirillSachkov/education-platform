using Shared.AI;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Infrastructure.AI.Configuration;
using MaterialProcessingService.Infrastructure.AI.ContentDrafts;
using MaterialProcessingService.Infrastructure.AI.Timecodes;

namespace MaterialProcessingService.IntegrationTests.Features;

public sealed class ContentAiRequestFactoryTests
{
    private readonly ContentAiRequestFactory _factory = new();

    [Fact]
    public void CreateSinglePassRequest_UsesMediumSummaryContract()
    {
        AiGenerationRequest request = _factory.CreateSinglePassRequest(
            CreateOptions(),
            CreateTranscript(),
            TimeSpan.FromMinutes(35));

        AssertMediumSummaryContract(request);
        AssertPromptContains("250–500 слов", request.SystemPrompt);
        AssertPromptContains("600–1200 слов", request.SystemPrompt);
        AssertPromptContains("до 1600 слов", request.SystemPrompt);
    }

    [Fact]
    public void CreateChunkRequest_SelectsOnlyImportantFragmentDetails()
    {
        TranscriptWindow window = new(
            StartSeconds: 0,
            EndSeconds: 900,
            Segments:
            [
                new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromMinutes(3), "Вводная часть"),
                new TranscriptSegment(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(9), "Ключевая мысль"),
            ]);

        AiGenerationRequest request = _factory.CreateChunkRequest(CreateOptions(), window, "ru");
        string prompt = CombinedPrompt(request);

        AssertPromptContains("СРЕДНИМ ПО ОБЪЁМУ", request.SystemPrompt);
        AssertPromptContains("Отбери 2–6 ключевых мыслей", request.SystemPrompt);
        AssertPromptContains("Сохраняй только важные нюансы", request.SystemPrompt);
        AssertPromptContains("обычно 120–250 слов", request.SystemPrompt);
        AssertPromptContains("Игнорируй приветствия", request.SystemPrompt);
        AssertPromptContains("шум на merge-шаг", request.SystemPrompt);
        Assert.False(prompt.Contains("Не игнорировать приветствия", StringComparison.OrdinalIgnoreCase), prompt);
        Assert.False(prompt.Contains("не выжимка", StringComparison.OrdinalIgnoreCase), prompt);
    }

    [Fact]
    public void CreateMergeRequest_UsesSameMediumSummaryContract()
    {
        AiGenerationRequest request = _factory.CreateMergeRequest(
            CreateOptions(),
            "ru",
            [
                new GeneratedContentChunk(0, 600, "**Кэш ускоряет чтение** — важны инвалидация и источник истины."),
                new GeneratedContentChunk(600, 1200, "**Outbox защищает события** — событие публикуется вместе с транзакцией."),
            ]);

        AssertMediumSummaryContract(request);
        AssertPromptContains("убирай повторы", request.UserPrompt ?? string.Empty);
    }

    private static void AssertMediumSummaryContract(AiGenerationRequest request)
    {
        string prompt = CombinedPrompt(request);

        AssertPromptContains("СРЕДНИЙ ПО ОБЪЁМУ", request.SystemPrompt);
        AssertPromptContains("4–8 пунктов", prompt);
        AssertPromptContains("3–6", prompt);
        AssertPromptContains("главная выжимка", prompt);
        AssertPromptContains("важные детали", prompt);
        Assert.False(prompt.Contains("5–10", StringComparison.OrdinalIgnoreCase), prompt);
        Assert.False(prompt.Contains("раскрывай КАЖДУЮ", StringComparison.OrdinalIgnoreCase), prompt);
    }

    private static void AssertPromptContains(string expected, string actual) =>
        Assert.Contains(expected, actual, StringComparison.Ordinal);

    private static string CombinedPrompt(AiGenerationRequest request) =>
        request.SystemPrompt + "\n" + request.UserPrompt;

    private static VideoProcessingAiModelOptions CreateOptions() => new()
    {
        Model = "test-chat-model",
        Provider = "aitunnel",
        Temperature = 0.2,
        MaxOutputTokens = 8000,
        TimeoutSeconds = 300,
    };

    private static Transcript CreateTranscript() => new(
        "ru",
        [
            new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromMinutes(5), "Приветствие и постановка проблемы"),
            new TranscriptSegment(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(20), "Разбор архитектурной идеи"),
            new TranscriptSegment(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(35), "Практические детали и ограничения"),
        ]);
}
