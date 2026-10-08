using MaterialProcessingService.Infrastructure.AI.Timecodes;

namespace MaterialProcessingService.IntegrationTests;

public sealed class TimecodePostProcessorTests
{
    [Fact]
    public void NormalizeFinalTimecodes_BoundsUntrustedAiCandidateCount()
    {
        TimecodeItemResponse[] candidates = Enumerable.Range(0, 2_000)
            .Select(index => new TimecodeItemResponse(
                StartSeconds: index,
                EndSeconds: index + 1,
                Title: $"Topic {index}",
                Confidence: 0.9))
            .ToArray();

        var normalized = TimecodePostProcessor.NormalizeFinalTimecodes(
            candidates,
            TimeSpan.FromHours(1));

        Assert.Equal(500, normalized.Length);
    }
}
