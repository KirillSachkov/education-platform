using Core.HttpCommunication;
using Microsoft.Extensions.Logging;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;

namespace ProgressService.Contracts.HttpCommunication;

internal sealed class ProgressServiceClient : BaseHttpClient, IProgressServiceClient
{
    private const string SERVICE_NAME = "ProgressService";

    public ProgressServiceClient(HttpClient httpClient, ILogger<ProgressServiceClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public async Task<Result<IReadOnlyDictionary<Guid, long>, Error>> GetMaterialViewsCountsAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken)
    {
        if (materialIds.Count == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, long>, Error>(new Dictionary<Guid, long>());
        }

        Result<GetMaterialViewsCountsResponse, Error> result = await PostAsync<GetMaterialViewsCountsRequest, GetMaterialViewsCountsResponse>(
            "/progress/materials/views/counts/",
            new GetMaterialViewsCountsRequest(materialIds),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error;
        }

        Dictionary<Guid, long> map = new(result.Value.Items.Count);
        foreach (MaterialViewsCountDto item in result.Value.Items)
        {
            map[item.MaterialId] = item.Count;
        }

        return Result.Success<IReadOnlyDictionary<Guid, long>, Error>(map);
    }
}
