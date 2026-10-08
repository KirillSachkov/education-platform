using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;
using MaterialProcessingService.Core;
using MaterialProcessingService.Core.Media;

namespace MaterialProcessingService.Infrastructure.FFmpeg;

internal sealed class FfmpegAudioExtractor : IAudioExtractor
{
    private readonly FfmpegProcessRunner _processRunner;
    private readonly FfmpegOptions _options;
    private readonly VideoProcessingOptions _processingOptions;
    private readonly ILogger<FfmpegAudioExtractor> _logger;

    public FfmpegAudioExtractor(
        FfmpegProcessRunner processRunner,
        IOptions<FfmpegOptions> options,
        IOptions<VideoProcessingOptions> processingOptions,
        ILogger<FfmpegAudioExtractor> logger)
    {
        _processRunner = processRunner;
        _options = options.Value;
        _processingOptions = processingOptions.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<AudioChunk>, Error>> ExtractChunksAsync(
        VideoProcessingSource source,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        string jobDirectory = GetJobDirectory(jobId);
        Directory.CreateDirectory(jobDirectory);

        string outputPattern = Path.Combine(jobDirectory, "chunk_%03d.mp3");

        ProcessRunResult result = await _processRunner.RunAsync(
            _options.BinaryPath,
            BuildExtractionArguments(source.Url, outputPattern),
            jobDirectory,
            cancellationToken);

        if (result.TimedOut)
        {
            return Error.Failure(
                "video.audio_extract.timeout",
                "Не удалось извлечь аудиодорожку: обработка видео заняла слишком много времени");
        }

        if (result.ExitCode != 0)
        {
            _logger.LogWarning(
                "ffmpeg audio extraction failed for video {VideoId} with exit code {ExitCode}; stderr length {ErrorLength}",
                source.VideoId,
                result.ExitCode,
                result.StandardError.Length);
            return Error.Failure("video.audio_extract.failed", "Не удалось извлечь аудиодорожку из видео");
        }

        string[] files = Directory.GetFiles(jobDirectory, "chunk_*.mp3")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
            return Error.Failure("video.audio_extract.empty", "После обработки видео не найдено ни одного аудиофрагмента");

        double speedup = ResolveSpeedup();

        var chunks = new List<AudioChunk>(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            chunks.Add(new AudioChunk(
                files[i],
                TimeSpan.FromSeconds(i * _processingOptions.ChunkSeconds),
                TimeSpan.FromSeconds(_processingOptions.ChunkSeconds),
                speedup));
        }

        return chunks;
    }

    public async Task<Result<IReadOnlyList<AudioChunk>, Error>> SplitChunkAsync(
        AudioChunk parent,
        int parts,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (parts < 2)
            return Error.Validation("video.audio_split.invalid_parts", "Количество частей должно быть >= 2");

        if (!File.Exists(parent.Path))
            return Error.Failure("video.audio_split.missing_parent", "Исходный mp3-чанк не найден");

        string jobDirectory = GetJobDirectory(jobId);
        Directory.CreateDirectory(jobDirectory);

        // Original-time длительность половины (для AudioChunk metadata).
        TimeSpan partDurationOrig = TimeSpan.FromSeconds(parent.Duration.TotalSeconds / parts);
        // Physical-time для ffmpeg -ss/-t — режем уже atempo'нутый файл, поэтому
        // делим на parent.Speedup (parent.Path физически в speedup раз короче).
        TimeSpan partDurationPhysical = TimeSpan.FromSeconds(partDurationOrig.TotalSeconds / parent.Speedup);
        string parentFileName = Path.GetFileNameWithoutExtension(parent.Path);
        var resultChunks = new List<AudioChunk>(parts);

        for (int i = 0; i < parts; i++)
        {
            TimeSpan partOffsetOrig = TimeSpan.FromSeconds(i * partDurationOrig.TotalSeconds);
            TimeSpan partOffsetPhysical = TimeSpan.FromSeconds(i * partDurationPhysical.TotalSeconds);
            string partPath = Path.Combine(
                jobDirectory,
                $"{parentFileName}_split{i}_{Guid.CreateVersion7():N}.mp3");

            ProcessRunResult splitResult = await _processRunner.RunAsync(
                _options.BinaryPath,
                BuildSplitArguments(parent.Path, partOffsetPhysical, partDurationPhysical, partPath),
                jobDirectory,
                cancellationToken);

            if (splitResult.TimedOut)
            {
                return Error.Failure(
                    "video.audio_split.timeout",
                    "Не удалось разделить mp3-чанк: ffmpeg превысил таймаут");
            }

            if (splitResult.ExitCode != 0)
            {
                _logger.LogWarning(
                    "ffmpeg split failed for chunk {ParentPath} part {PartIndex} with exit code {ExitCode}; stderr length {ErrorLength}",
                    parent.Path,
                    i,
                    splitResult.ExitCode,
                    splitResult.StandardError.Length);
                return Error.Failure(
                    "video.audio_split.failed",
                    "Не удалось разделить mp3-чанк");
            }

            resultChunks.Add(new AudioChunk(
                partPath,
                parent.Offset + partOffsetOrig,
                partDurationOrig,
                parent.Speedup));
        }

        return resultChunks;
    }

    private static IReadOnlyList<string> BuildSplitArguments(
        string sourcePath,
        TimeSpan offset,
        TimeSpan duration,
        string outputPath) =>
    [
        "-y",
        "-nostdin",
        "-ss", offset.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-t", duration.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-i", sourcePath,
        "-c", "copy",
        outputPath,
    ];

    public Task CleanupAsync(Guid jobId, CancellationToken cancellationToken)
    {
        string jobDirectory = GetJobDirectory(jobId);

        if (Directory.Exists(jobDirectory))
        {
            try
            {
                Directory.Delete(jobDirectory, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup FFmpeg job directory {JobDirectory}", jobDirectory);
            }
        }

        return Task.CompletedTask;
    }

    private string GetJobDirectory(Guid jobId)
    {
        string root = string.IsNullOrWhiteSpace(_options.TempRootPath)
            ? Path.Combine(Path.GetTempPath(), "timecode-jobs")
            : _options.TempRootPath;

        return Path.Combine(root, jobId.ToString("N"));
    }

    private IReadOnlyList<string> BuildExtractionArguments(string sourceUrl, string outputPattern)
    {
        double speedup = ResolveSpeedup();

        // segment_time идёт по PHYSICAL trim'нутому аудио (после atempo). Чтобы chunk
        // покрывал ChunkSeconds ОРИГИНАЛЬНОГО времени (как и предполагает остальной
        // код через AudioChunk.Duration), ужимаем segment_time на тот же speedup.
        // 900s × 1.2 = 750s physical → каждый chunk покрывает ровно 900s оригинала.
        double physicalSegmentSeconds = _processingOptions.ChunkSeconds / speedup;
        string segmentSeconds = physicalSegmentSeconds
            .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-y",
            "-nostdin",
            "-fflags", "+discardcorrupt+genpts",
            "-err_detect", "ignore_err",
            "-analyzeduration", "200M",
            "-probesize", "200M",
            "-i", sourceUrl,
            "-map", "0:a:0",
            "-vn",
            "-ac", "1",
            "-ar", "48000",
        };

        // atempo сжимает audio в speedup раз без сдвига pitch (WSOLA). На [0.5, 2.0]
        // качество стабильно; вне диапазона ffmpeg цепляет 2-й atempo и теряет
        // согласованность с нашим маппингом (2 фильтра ≠ 1×speedup в timestamp scale).
        // ResolveSpeedup() уже клампит в [0.5, 2.0].
        if (Math.Abs(speedup - 1.0) > 0.001)
        {
            args.Add("-af");
            args.Add($"atempo={speedup.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        args.AddRange(new[]
        {
            "-c:a", "libmp3lame",
            "-q:a", "4",
            "-f", "segment",
            "-segment_format", "mp3",
            "-segment_time", segmentSeconds,
            "-reset_timestamps", "1",
            outputPattern
        });

        return args;
    }

    /// <summary>
    ///     Возвращает безопасное значение atempo-множителя из настроек, клампя в
    ///     [0.5, 2.0] (ffmpeg atempo single-filter limit). 1.0 — отключение фильтра.
    /// </summary>
    private double ResolveSpeedup()
    {
        double speedup = _processingOptions.AudioSpeedup;
        if (double.IsNaN(speedup) || speedup <= 0)
            return 1.0;
        return Math.Clamp(speedup, 0.5, 2.0);
    }
}
