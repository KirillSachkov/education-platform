namespace MaterialProcessingService.Core.Media;

public sealed record MediaProbeResult(
    bool HasAudioStream,
    TimeSpan Duration);
