using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;
using MaterialProcessingService.Core.Media;

namespace MaterialProcessingService.Infrastructure.FFmpeg;

internal sealed class FfprobeMediaProbe : IMediaProbe
{
    private readonly FfmpegProcessRunner _processRunner;
    private readonly FfmpegOptions _options;
    private readonly ILogger<FfprobeMediaProbe> _logger;

    public FfprobeMediaProbe(
        FfmpegProcessRunner processRunner,
        IOptions<FfmpegOptions> options,
        ILogger<FfprobeMediaProbe> logger)
    {
        _processRunner = processRunner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<MediaProbeResult, Error>> ProbeAsync(
        VideoProcessingSource source,
        CancellationToken cancellationToken)
    {
        ProcessRunResult result = await _processRunner.RunAsync(
            _options.ProbeBinaryPath,
            [
                "-v", "error",
                "-print_format", "json",
                "-show_format",
                "-show_streams",
                source.Url
            ],
            null,
            cancellationToken);

        if (result.TimedOut)
        {
            return Error.Failure(
                "video.probe.timeout",
                "Не удалось прочитать видео: проверка заняла слишком много времени");
        }

        if (result.ExitCode != 0)
        {
            if (ContainsProtectedSourceError(result.StandardError))
            {
                _logger.LogWarning(
                    "Protected or unreadable processing source detected for video {VideoId}",
                    source.VideoId);

                return Error.Failure(
                    "video.processing_source.unavailable",
                    "Источник видео недоступен для обработки. Проверьте настройки доступа у видео в Kinescope");
            }

            _logger.LogWarning(
                "ffprobe failed for video {VideoId} with exit code {ExitCode}; stderr length {ErrorLength}",
                source.VideoId,
                result.ExitCode,
                result.StandardError.Length);
            return Error.Failure("video.probe.failed", "Не удалось прочитать видео перед генерацией тайм-кодов");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);

            JsonElement? audioStream = null;
            if (document.RootElement.TryGetProperty("streams", out JsonElement streamsElement) &&
                streamsElement.ValueKind == JsonValueKind.Array)
            {
                audioStream = streamsElement.EnumerateArray().FirstOrDefault(stream =>
                    stream.TryGetProperty("codec_type", out JsonElement codecType) &&
                    string.Equals(codecType.GetString(), "audio", StringComparison.Ordinal));
            }

            bool hasAudioStream = audioStream.HasValue;

            if (audioStream.HasValue &&
                audioStream.Value.TryGetProperty("channels", out JsonElement channelsElement) &&
                channelsElement.TryGetInt32(out int channels) &&
                channels > 8)
            {
                _logger.LogWarning(
                    "Unsupported audio channel layout detected for video {VideoId}: {Channels} channels",
                    source.VideoId,
                    channels);

                return Error.Failure(
                    "video.audio_stream.invalid",
                    "Аудиодорожка видео имеет неподдерживаемый формат для обработки");
            }

            TimeSpan duration = TimeSpan.Zero;
            if (document.RootElement.TryGetProperty("format", out JsonElement formatElement) &&
                formatElement.TryGetProperty("duration", out JsonElement durationElement) &&
                double.TryParse(durationElement.GetString(), out double durationSeconds))
            {
                duration = TimeSpan.FromSeconds(durationSeconds);
            }
            else if (source.DurationSeconds.HasValue)
            {
                duration = TimeSpan.FromSeconds(source.DurationSeconds.Value);
            }

            return new MediaProbeResult(hasAudioStream, duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse ffprobe output for video {VideoId}", source.VideoId);
            return Error.Failure("video.probe.invalid", "Не удалось разобрать метаданные видео");
        }
    }

    private static bool ContainsProtectedSourceError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return false;

        return stderr.Contains("Unable to open key file", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("sample-aes", StringComparison.OrdinalIgnoreCase);
    }
}
