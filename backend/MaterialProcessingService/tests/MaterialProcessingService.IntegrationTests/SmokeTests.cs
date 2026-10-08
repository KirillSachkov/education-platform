using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.AI;
using MaterialProcessingService.Core;
using MaterialProcessingService.Core.Subtitles;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Infrastructure.AI.Configuration;

namespace MaterialProcessingService.IntegrationTests;

public sealed class SmokeTests
{
    [Fact]
    public void Placeholder()
    {
        Assert.True(true);
    }

    [Fact]
    public void AddAiGeneration_Throws_WhenProviderApiKeyIsMissing()
    {
        ServiceCollection services = [];
        IConfiguration configuration = BuildAiConfiguration(
            new KeyValuePair<string, string?>("AI:Providers:aitunnel:ApiKey", string.Empty));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => services.AddAiGeneration(configuration));

        Assert.Contains("ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAiGeneration_Throws_WhenRequiredServiceModelIsMissing()
    {
        ServiceCollection services = [];
        IConfiguration configuration = BuildAiConfiguration(
            new KeyValuePair<string, string?>("VideoProcessingAI:SpeechToText:Model", null));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => services.AddAiGeneration(configuration));

        Assert.Contains("VideoProcessingAI:SpeechToText", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAiGeneration_RegistersAiClientFactory()
    {
        ServiceCollection services = [];
        services.AddLogging();
        services.AddAiGeneration(BuildAiConfiguration());

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        // After issue #146 multi-provider switch, IAiClient is no longer DI-registered;
        // services resolve via IAiClientFactory.Get(providerName).
        IAiClientFactory factory = serviceProvider.GetRequiredService<IAiClientFactory>();
        IAiClient aiClient = factory.Get(null);

        Assert.NotNull(aiClient);
    }

    [Fact]
    public void AddCore_RegistersSubtitleRenderer()
    {
        ServiceCollection services = [];
        services.AddCore(new ConfigurationBuilder().AddInMemoryCollection().Build());

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        ISubtitleRenderer renderer = serviceProvider.GetRequiredService<ISubtitleRenderer>();
        string srt = renderer.RenderSrt(new Transcript(
            "ru",
            [
                new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2.5), "Привет"),
                new TranscriptSegment(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5), "Мир")
            ]));

        // Subtitle renderer uses Environment.NewLine — на CI Linux это LF, локально на
        // macOS тоже LF, на Windows — CRLF. Сравниваем по нормализованной форме чтобы
        // тест был кросс-платформенным.
        string normalized = srt.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(
            "1\n00:00:00,000 --> 00:00:02,500\nПривет\n\n2\n00:00:02,500 --> 00:00:05,000\nМир\n",
            normalized);
    }

    private static IConfiguration BuildAiConfiguration(params KeyValuePair<string, string?>[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["AI:Default"] = "aitunnel",
            ["AI:Providers:aitunnel:Kind"] = "OpenAiCompatible",
            ["AI:Providers:aitunnel:BaseUrl"] = "https://routerai.test/api/v1/",
            ["AI:Providers:aitunnel:TimeoutSeconds"] = "30",
            ["AI:Providers:aitunnel:ApiKey"] = "test-api-key",
            ["VideoProcessingAI:SpeechToText:Provider"] = "aitunnel",
            ["VideoProcessingAI:SpeechToText:Model"] = "test-audio-model",
            ["VideoProcessingAI:SpeechToText:Temperature"] = "0",
            ["VideoProcessingAI:SpeechToText:MaxOutputTokens"] = "4000",
            ["VideoProcessingAI:SpeechToText:TimeoutSeconds"] = "900",
            ["VideoProcessingAI:TimecodeGeneration:Provider"] = "aitunnel",
            ["VideoProcessingAI:TimecodeGeneration:Model"] = "test-chat-model",
            ["VideoProcessingAI:TimecodeGeneration:Temperature"] = "0.1",
            ["VideoProcessingAI:TimecodeGeneration:MaxOutputTokens"] = "6000",
            ["VideoProcessingAI:TimecodeGeneration:TimeoutSeconds"] = "300",
            ["VideoProcessingAI:ContentGeneration:Provider"] = "aitunnel",
            ["VideoProcessingAI:ContentGeneration:Model"] = "test-chat-model",
            ["VideoProcessingAI:ContentGeneration:Temperature"] = "0.2",
            ["VideoProcessingAI:ContentGeneration:MaxOutputTokens"] = "8000",
            ["VideoProcessingAI:ContentGeneration:TimeoutSeconds"] = "300",
        };

        foreach (KeyValuePair<string, string?> item in overrides)
        {
            if (item.Value is null)
                values.Remove(item.Key);
            else
                values[item.Key] = item.Value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
