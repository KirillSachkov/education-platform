using CSharpFunctionalExtensions;
using SharedKernel;
using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Core.Features.Timecodes;

public interface ITimecodeGenerator
{
    Task<Result<GeneratedTimecodesResult, Error>> GenerateAsync(
        Transcript transcript,
        TimeSpan duration,
        string? modelOverride,
        CancellationToken cancellationToken);
}
