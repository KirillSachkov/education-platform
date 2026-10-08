using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaterialProcessingService.Infrastructure.FFmpeg;

internal sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false);

internal sealed class FfmpegProcessRunner
{
    private readonly FfmpegOptions _options;
    private readonly ILogger<FfmpegProcessRunner> _logger;

    public FfmpegProcessRunner(
        IOptions<FfmpegOptions> options,
        ILogger<FfmpegProcessRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ProcessTimeoutSeconds));

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);

            string timedOutStdout = await ReadProcessOutputAsync(stdoutTask);
            string timedOutStderr = await ReadProcessOutputAsync(stderrTask);

            _logger.LogWarning(
                ex,
                "Process {FileName} timed out after {TimeoutSeconds} seconds",
                fileName,
                _options.ProcessTimeoutSeconds);

            return new ProcessRunResult(-1, timedOutStdout, timedOutStderr, TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            throw;
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        _logger.LogDebug(
            "Process {FileName} finished with code {ExitCode}",
            fileName,
            process.ExitCode);

        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }

    private static async Task<string> ReadProcessOutputAsync(Task<string> outputTask)
    {
        try
        {
            return await outputTask;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort cleanup. The caller receives a timed-out result or cancellation.
        }
    }
}
