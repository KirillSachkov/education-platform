using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.Extensions.Caching.Hybrid;

namespace EducationContentService.Core.Features.FileEvents;

internal sealed class CachedFileServiceClient : IFileServiceClient
{
    private const int MAX_FILE_BATCH_SIZE = 50;

    internal const string FILE_CACHE_KEY_PREFIX = "file-service:file:";
    internal const string VIDEO_DETAIL_CACHE_KEY_PREFIX = "file-service:video-detail:";
    internal const string VIDEO_PUBLIC_CACHE_KEY_PREFIX = "file-service:video-public:";

    private readonly IFileServiceClient _inner;
    private readonly HybridCache _cache;

    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    public CachedFileServiceClient(IFileServiceClient inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<Result<GetFileResponse?, Error>> GetFileAsync(
        Guid fileId, CancellationToken cancellationToken)
    {
        string key = $"{FILE_CACHE_KEY_PREFIX}{fileId}";
        Error? fetchError = null;

        GetFileResponse? cached = await _cache.GetOrCreateAsync(
            key,
            async ct =>
            {
                Result<GetFileResponse?, Error> result = await _inner.GetFileAsync(fileId, ct);
                if (result.IsFailure)
                {
                    fetchError = result.Error;
                    return null;
                }
                return result.Value;
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        if (fetchError is not null)
        {
            await _cache.RemoveAsync(key, cancellationToken);
            return fetchError;
        }

        return Result.Success<GetFileResponse?, Error>(cached);
    }

    public async Task<Result<GetVideoResponse?, Error>> GetVideoAsync(
        Guid videoId, CancellationToken cancellationToken)
    {
        string key = $"{VIDEO_DETAIL_CACHE_KEY_PREFIX}{videoId}";
        Error? fetchError = null;

        GetVideoResponse? cached = await _cache.GetOrCreateAsync(
            key,
            async ct =>
            {
                Result<GetVideoResponse?, Error> result = await _inner.GetVideoAsync(videoId, ct);
                if (result.IsFailure)
                {
                    fetchError = result.Error;
                    return null;
                }
                return result.Value;
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        if (fetchError is not null)
        {
            await _cache.RemoveAsync(key, cancellationToken);
            return fetchError;
        }

        return Result.Success<GetVideoResponse?, Error>(cached);
    }

    public Task<Result<GetVideoProcessingSourceResponse?, Error>> GetVideoProcessingSourceAsync(
        Guid videoId,
        CancellationToken cancellationToken)
        => _inner.GetVideoProcessingSourceAsync(videoId, cancellationToken);

    public Task<UnitResult<Error>> UpdateChaptersAsync(
        Guid videoId,
        UpdateVideoChaptersRequest request,
        CancellationToken cancellationToken)
        => _inner.UpdateChaptersAsync(videoId, request, cancellationToken);

    // Не кэшируем — вызывается только backfill-CLI'ями вне горячего пути.
    public Task<Result<GetVideoChaptersResponse?, Error>> GetVideoChaptersAsync(
        Guid videoId,
        CancellationToken cancellationToken)
        => _inner.GetVideoChaptersAsync(videoId, cancellationToken);

    public Task<Result<GetActiveAssetSlotResponse?, Error>> GetActiveAssetSlotAsync(
        string entityType,
        Guid entityId,
        string usageType,
        CancellationToken cancellationToken) =>
        _inner.GetActiveAssetSlotAsync(entityType, entityId, usageType, cancellationToken);

    public async Task<Result<List<GetPublicVideoResponse>?, Error>> GetVideosBatchAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var hits = new List<GetPublicVideoResponse>(ids.Count);
        var missIds = new List<Guid>(ids.Count);

        foreach (Guid id in ids)
        {
            string key = $"{VIDEO_PUBLIC_CACHE_KEY_PREFIX}{id}";
            bool factoryCalled = false;

            GetPublicVideoResponse? cached = await _cache.GetOrCreateAsync<GetPublicVideoResponse?>(
                key,
                _ =>
                {
                    factoryCalled = true;
                    return ValueTask.FromResult<GetPublicVideoResponse?>(null);
                },
                _cacheOptions,
                cancellationToken: cancellationToken);

            if (!factoryCalled && cached is not null)
            {
                hits.Add(cached);
            }
            else
            {
                await _cache.RemoveAsync(key, cancellationToken);
                missIds.Add(id);
            }
        }

        if (missIds.Count == 0)
        {
            return hits;
        }

        Result<List<GetPublicVideoResponse>?, Error> fetchResult =
            await _inner.GetVideosBatchAsync(missIds, cancellationToken);

        if (fetchResult.IsFailure)
        {
            return fetchResult.Error;
        }

        if (fetchResult.Value is not null)
        {
            foreach (GetPublicVideoResponse video in fetchResult.Value)
            {
                string key = $"{VIDEO_PUBLIC_CACHE_KEY_PREFIX}{video.Id}";
                await _cache.SetAsync(key, video, _cacheOptions, cancellationToken: cancellationToken);
                hits.Add(video);
            }
        }

        Dictionary<Guid, int> idOrder = ids
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i);
        hits = [.. hits.OrderBy(r => idOrder.GetValueOrDefault(r.Id, int.MaxValue))];

        return hits;
    }

    public Task<Result<List<GetFileResponse>?, Error>> GetFilesByEntityAsync(
        Guid entityId, string entityType, CancellationToken cancellationToken)
        => _inner.GetFilesByEntityAsync(entityId, entityType, cancellationToken);

    public async Task<Result<List<GetFileResponse>?, Error>> GetFilesBatchAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        // Split ids into cache hits and misses. HybridCache has no TryGet — probe via
        // GetOrCreateAsync with a null-returning factory. On cache hit the factory is
        // never called; on miss it fires and we track it via flag.
        var hits = new List<GetFileResponse>(ids.Count);
        var missIds = new List<Guid>(ids.Count);

        foreach (Guid id in ids)
        {
            string key = $"{FILE_CACHE_KEY_PREFIX}{id}";
            bool factoryCalled = false;

            GetFileResponse? cached = await _cache.GetOrCreateAsync<GetFileResponse?>(
                key,
                _ =>
                {
                    factoryCalled = true;
                    return ValueTask.FromResult<GetFileResponse?>(null);
                },
                _cacheOptions,
                cancellationToken: cancellationToken);

            if (!factoryCalled && cached is not null)
            {
                hits.Add(cached);
            }
            else
            {
                await _cache.RemoveAsync(key, cancellationToken);
                missIds.Add(id);
            }
        }

        if (missIds.Count == 0)
        {
            return hits;
        }

        foreach (Guid[] chunk in missIds.Chunk(MAX_FILE_BATCH_SIZE))
        {
            Result<List<GetFileResponse>?, Error> fetchResult =
                await _inner.GetFilesBatchAsync(chunk, cancellationToken);

            if (fetchResult.IsFailure)
                return fetchResult.Error;

            if (fetchResult.Value is null)
                continue;

            foreach (GetFileResponse file in fetchResult.Value)
            {
                string key = $"{FILE_CACHE_KEY_PREFIX}{file.Id}";
                await _cache.SetAsync(key, file, _cacheOptions, cancellationToken: cancellationToken);
                hits.Add(file);
            }
        }

        Dictionary<Guid, int> idOrder = ids
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i);
        hits = [.. hits.OrderBy(r => idOrder.GetValueOrDefault(r.Id, int.MaxValue))];

        return hits;
    }

    public Task<UnitResult<Error>> BindDraftAssetsAsync(
        BindDraftAssetsRequest request, CancellationToken cancellationToken)
        => _inner.BindDraftAssetsAsync(request, cancellationToken);

    public Task<UnitResult<Error>> SyncEntityAssetsAsync(
        SyncEntityAssetsRequest request, CancellationToken cancellationToken)
        => _inner.SyncEntityAssetsAsync(request, cancellationToken);

    // Side-effect ops — pass-through, no caching. Invalidate the asset cache so that
    // subsequent GetFile/GetVideo reads after a bind/detach see fresh state instead of
    // a stale snapshot from the 5-min HybridCache window.
    public async Task<Result<BindAssetResponse, Error>> BindAssetAsync(
        Guid assetId, BindAssetRequest request, CancellationToken cancellationToken)
    {
        Result<BindAssetResponse, Error> result = await _inner.BindAssetAsync(assetId, request, cancellationToken);
        if (result.IsSuccess)
        {
            await _cache.RemoveAsync($"{FILE_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
            await _cache.RemoveAsync($"{VIDEO_DETAIL_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
            await _cache.RemoveAsync($"{VIDEO_PUBLIC_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
        }
        return result;
    }

    public async Task<UnitResult<Error>> DetachAssetAsync(
        Guid assetId, CancellationToken cancellationToken)
    {
        UnitResult<Error> result = await _inner.DetachAssetAsync(assetId, cancellationToken);
        if (result.IsSuccess)
        {
            await _cache.RemoveAsync($"{FILE_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
            await _cache.RemoveAsync($"{VIDEO_DETAIL_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
            await _cache.RemoveAsync($"{VIDEO_PUBLIC_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
        }
        return result;
    }

    public async Task<Result<BindAssetResponse, Error>> BindAssetInternalAsync(
        Guid assetId,
        BindAssetInternalRequest request,
        CancellationToken cancellationToken)
    {
        Result<BindAssetResponse, Error> result = await _inner.BindAssetInternalAsync(
            assetId,
            request,
            cancellationToken);
        if (result.IsSuccess)
        {
            await _cache.RemoveAsync($"{FILE_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
            await _cache.RemoveAsync($"{VIDEO_DETAIL_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
            await _cache.RemoveAsync($"{VIDEO_PUBLIC_CACHE_KEY_PREFIX}{assetId}", cancellationToken);
        }
        return result;
    }

    public Task<UnitResult<Error>> ReassignAssetsOwnerAsync(
        ReassignAssetOwnerRequest request,
        CancellationToken cancellationToken)
        => _inner.ReassignAssetsOwnerAsync(request, cancellationToken);
}
