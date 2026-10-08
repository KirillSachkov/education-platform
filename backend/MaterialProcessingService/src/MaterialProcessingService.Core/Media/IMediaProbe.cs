using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Core.Media;

public interface IMediaProbe
{
    Task<Result<MediaProbeResult, Error>> ProbeAsync(
        VideoProcessingSource source,
        CancellationToken cancellationToken);
}
