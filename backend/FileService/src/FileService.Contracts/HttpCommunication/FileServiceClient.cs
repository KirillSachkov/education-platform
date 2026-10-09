using System.Net.Http.Json;
using System.Text.Json;
using Core.HttpCommunication;
using FileService.Contracts.Assets;
using Microsoft.Extensions.Logging;

namespace FileService.Contracts.HttpCommunication;

internal sealed class FileServiceClient : BaseHttpClient, IFileServiceClient
{
    private const string SERVICE_NAME = "FileService";

    public FileServiceClient(
        HttpClient httpClient,
        ILogger<FileServiceClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public Task<Result<GetVideoResponse?, Error>> GetVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken)
        => GetAsync<GetVideoResponse?>($"/internal/videos/{videoId}/", cancellationToken);

    public async Task<UnitResult<Error>> UpdateChaptersAsync(
        Guid videoId,
        UpdateVideoChaptersRequest request,
        CancellationToken cancellationToken)
    {
        Result<GetVideoChaptersResponse, Error> result = await PutAsync<UpdateVideoChaptersRequest, GetVideoChaptersResponse>(
            $"/internal/videos/{videoId}/chapters/",
            request,
            cancellationToken);

        return result.IsSuccess
            ? UnitResult.Success<Error>()
            : result.Error;
    }

    public Task<Result<GetVideoChaptersResponse?, Error>> GetVideoChaptersAsync(
        Guid videoId,
        CancellationToken cancellationToken)
        => GetAsync<GetVideoChaptersResponse?>($"/videos/{videoId}/chapters/", cancellationToken);

    public Task<Result<List<GetPublicVideoResponse>?, Error>> GetVideosBatchAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return Task.FromResult(Result.Success<List<GetPublicVideoResponse>?, Error>(new List<GetPublicVideoResponse>()));

        string queryString = string.Join("&", ids.Select(id => $"ids={id}"));
        return GetAsync<List<GetPublicVideoResponse>?>($"/internal/videos/batch/?{queryString}", cancellationToken);
    }

    public Task<Result<GetActiveAssetSlotResponse?, Error>> GetActiveAssetSlotAsync(
        string entityType,
        Guid entityId,
        string usageType,
        CancellationToken cancellationToken) =>
        GetAsync<GetActiveAssetSlotResponse?>(
            $"/internal/assets/active-slot/?entityType={Uri.EscapeDataString(entityType)}&entityId={entityId}&usageType={Uri.EscapeDataString(usageType)}",
            cancellationToken);

    public Task<Result<GetFileResponse?, Error>> GetFileAsync(
        Guid fileId,
        CancellationToken cancellationToken)
        => GetAsync<GetFileResponse?>($"/internal/files/{fileId}/", cancellationToken);

    public Task<Result<List<GetFileResponse>?, Error>> GetFilesByEntityAsync(
        Guid entityId,
        string entityType,
        CancellationToken cancellationToken)
        => GetAsync<List<GetFileResponse>?>($"/files/by-entity?entityId={entityId}&entityType={entityType}", cancellationToken);

    public Task<Result<List<GetFileResponse>?, Error>> GetFilesBatchAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return Task.FromResult(Result.Success<List<GetFileResponse>?, Error>(new List<GetFileResponse>()));

        string queryString = string.Join("&", ids.Select(id => $"ids={id}"));
        return GetAsync<List<GetFileResponse>?>($"/internal/files/batch/?{queryString}", cancellationToken);
    }

    public async Task<UnitResult<Error>> BindDraftAssetsAsync(
        BindDraftAssetsRequest request,
        CancellationToken cancellationToken)
    {
        Result<int, Error> result = await PostAsync<BindDraftAssetsRequest, int>(
            "/draft-assets/bind", request, cancellationToken);

        return result.IsSuccess ? UnitResult.Success<Error>() : result.Error;
    }

    public async Task<UnitResult<Error>> SyncEntityAssetsAsync(
        SyncEntityAssetsRequest request,
        CancellationToken cancellationToken)
    {
        Result<int, Error> result = await PostAsync<SyncEntityAssetsRequest, int>(
            "/assets/sync", request, cancellationToken);

        return result.IsSuccess ? UnitResult.Success<Error>() : result.Error;
    }

    public async Task<Result<BindAssetResponse, Error>> BindAssetAsync(
        Guid assetId,
        BindAssetRequest request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            $"/files/{assetId}/bind/", request, cancellationToken);
        return await ToBindAssetResultAsync(response, cancellationToken);
    }

    public async Task<Result<BindAssetResponse, Error>> BindAssetInternalAsync(
        Guid assetId,
        BindAssetInternalRequest request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            $"/internal/assets/{assetId}/bind/", request, cancellationToken);

        // Consumer-first rolling deploy: the old FileService has no internal bind
        // route. Its public route is the legacy path ECS used and still validates
        // uploader ownership; retry it only while the internal route returns 404.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            response = await HttpClient.PostAsJsonAsync(
                $"/files/{assetId}/bind/",
                new BindAssetRequest(request.TargetEntity),
                cancellationToken);
        }

        return await ToBindAssetResultAsync(response, cancellationToken);
    }

    public async Task<UnitResult<Error>> DetachAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        // No body — endpoint takes no [FromBody] parameter. Sending `null` JSON
        // would serialize to the string "null" and pollute the request log.
        HttpResponseMessage response = await HttpClient.PostAsync(
            $"/files/{assetId}/detach", content: null, cancellationToken);
        return await ToUnitResultAsync(response, cancellationToken);
    }

    public async Task<UnitResult<Error>> ReassignAssetsOwnerAsync(
        ReassignAssetOwnerRequest request,
        CancellationToken cancellationToken)
    {
        Result<int, Error> result = await PostAsync<ReassignAssetOwnerRequest, int>(
            "/internal/assets/reassign-owner", request, cancellationToken);

        return result.IsSuccess ? UnitResult.Success<Error>() : result.Error;
    }

    /// <summary>
    ///     Maps a "ok / error envelope without payload" response to <see cref="UnitResult{T}"/>.
    ///     <see cref="BaseHttpClient"/>'s <c>HandleResponseAsync&lt;TResponse&gt;</c> requires
    ///     a non-null <c>result</c> and is not suited for handlers that return
    ///     <see cref="UnitResult{T}"/> with no payload — this helper covers that gap
    ///     by checking status code and parsing the envelope only when it carries an error.
    /// </summary>
    private static async Task<UnitResult<Error>> ToUnitResultAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return UnitResult.Success<Error>();

        try
        {
            Envelope<int>? envelope = await response.Content
                .ReadFromJsonAsync<Envelope<int>>(cancellationToken);
            if (envelope?.Error is { } envError)
                return envError;
        }
        catch (JsonException)
        {
            // Fall through to generic error below.
        }

        return Error.Failure(
            "http.unknown_error",
            $"FileService returned {(int)response.StatusCode}");
    }

    private static async Task<Result<BindAssetResponse, Error>> ToBindAssetResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            UnitResult<Error> errorResult = await ToUnitResultAsync(response, cancellationToken);
            return errorResult.Error;
        }

        try
        {
            Envelope<BindAssetResponse>? envelope = await response.Content
                .ReadFromJsonAsync<Envelope<BindAssetResponse>>(cancellationToken);

            // Rolling compatibility: the pre-revision FileService returned a successful
            // Unit envelope (`result: null`). ECS stores revision 0 until FileService is
            // migrated/deployed and the asset is selected again.
            return envelope?.Result ?? new BindAssetResponse(0);
        }
        catch (JsonException)
        {
            return Error.Failure(
                "http.invalid_json",
                "FileService returned an invalid bind response");
        }
    }
}