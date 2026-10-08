namespace MaterialProcessingService.Infrastructure.FFmpeg;

public sealed class FfmpegOptions
{
    public const string SECTION_NAME = "Ffmpeg";

    public string BinaryPath { get; set; } = "ffmpeg";

    public string ProbeBinaryPath { get; set; } = "ffprobe";

    public int ProcessTimeoutSeconds { get; set; } = 1800;

    public string? TempRootPath { get; set; }
}
