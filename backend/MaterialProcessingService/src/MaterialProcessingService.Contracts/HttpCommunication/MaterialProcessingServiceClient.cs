using Core.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using MaterialProcessingService.Contracts.Timecodes.Dtos;

namespace MaterialProcessingService.Contracts.HttpCommunication;

internal sealed class MaterialProcessingServiceClient : BaseHttpClient, IMaterialProcessingServiceClient
{
    private const string SERVICE_NAME = "MaterialProcessingService";

    public MaterialProcessingServiceClient(
        HttpClient httpClient,
        ILogger<MaterialProcessingServiceClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public Task<Result<GetVideoTimecodesResponse, Error>> GetVideoTimecodesAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        GetAsync<GetVideoTimecodesResponse>(
            $"/material-processing/videos/{videoId}/",
            cancellationToken);

    public Task<Result<GetVideoArtifactStatusesResponse, Error>> GetArtifactStatusesAsync(
        GetVideoArtifactStatusesRequest request,
        CancellationToken cancellationToken) =>
        PostAsync<GetVideoArtifactStatusesRequest, GetVideoArtifactStatusesResponse>(
            "/material-processing/artifacts/by-videos/",
            request,
            cancellationToken);
}
