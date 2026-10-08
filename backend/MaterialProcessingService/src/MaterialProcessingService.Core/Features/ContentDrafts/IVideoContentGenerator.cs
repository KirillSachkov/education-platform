using CSharpFunctionalExtensions;
using SharedKernel;
using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Core.Features.ContentDrafts;

public interface IVideoContentGenerator
{
    Task<Result<GeneratedVideoContentResult, Error>> GenerateAsync(
        Transcript transcript,
        TimeSpan duration,
        string? modelOverride,
        CancellationToken cancellationToken);
}
