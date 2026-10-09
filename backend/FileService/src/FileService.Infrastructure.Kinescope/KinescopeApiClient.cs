using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using FileService.Core.Features.Videos;
using FileService.Core.Services;
using FileService.Infrastructure.Kinescope.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace FileService.Infrastructure.Kinescope;

public sealed class KinescopeApiClient : IVideoProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<KinescopeApiClient> _logger;
    private readonly KinescopeOptions _options;

    public KinescopeApiClient(
        HttpClient httpClient,
        IOptions<KinescopeOptions> options,
        ILogger<KinescopeApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<VideoUploadInitResult, Error>> InitiateUploadAsync(
        string title,
        string fileName,
        long fileSize,
        string? clientIp = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            type = KinescopeConstants.VIDEO_TYPE,
            title,
            filename = fileName,
            filesize = fileSize,
            parent_id = _options.ParentId,
            client_ip = clientIp,
        };

        using HttpRequestMessage request = new(HttpMethod.Post, $"{_options.UploaderBaseUrl}/v2/init")
        {
            Content = JsonContent.Create(requestBody, options: _jsonOptions),
        };

        request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Kinescope upload init failed with status {StatusCode}", response.StatusCode);
                return Error.Failure("kinescope.upload.init.failed", "Ошибка при инициализации загрузки видео");
            }

            KinescopeInitResponse? result = await response.Content.ReadFromJsonAsync<KinescopeInitResponse>(
                _jsonOptions,
                cancellationToken);

            if (result?.Data is null)
            {
                return Error.Failure("kinescope.upload.init.invalid", "Некорректный ответ Kinescope при инициализации");
            }

            return new VideoUploadInitResult(result.Data.Id, result.Data.Endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kinescope upload initialization failed");
            return Error.Failure("kinescope.upload.init.exception", "Внутренняя ошибка при инициализации загрузки видео");
        }
    }

    public async Task<Result<VideoProviderAssetInfo, Error>> GetStatusAsync(
        string externalAssetId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{_options.ApiBaseUrl}/v1/videos/{externalAssetId}");
        request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Kinescope video {ExternalAssetId} not found", externalAssetId);
                return Error.NotFound("kinescope.video.not.found", "Видео не найдено");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Kinescope video info request failed with status {StatusCode} for {ExternalAssetId}", response.StatusCode, externalAssetId);
                return Error.Failure("kinescope.video.info.failed", "Ошибка получения информации о видео");
            }

            KinescopeVideoResponse? result = await response.Content.ReadFromJsonAsync<KinescopeVideoResponse>(
                _jsonOptions,
                cancellationToken);

            if (result?.Data is null)
            {
                return Error.Failure("kinescope.video.info.invalid", "Некорректный ответ Kinescope");
            }

            return MapToInfo(result.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kinescope status request failed for {ExternalAssetId}", externalAssetId);
            return Error.Failure("kinescope.video.info.exception", "Внутренняя ошибка при получении информации о видео");
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, VideoProviderAssetInfo>, Error>> GetStatusesBatchAsync(
        IReadOnlyList<string> externalAssetIds,
        CancellationToken cancellationToken = default)
    {
        if (externalAssetIds.Count == 0)
        {
            return new Dictionary<string, VideoProviderAssetInfo>();
        }

        string queryParams = string.Join("&", externalAssetIds.Select(id => $"video_ids[]={Uri.EscapeDataString(id)}"));
        string url = $"{_options.ApiBaseUrl}/v1/videos?{queryParams}&per_page={externalAssetIds.Count}";

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Kinescope batch status request failed with status {StatusCode}", response.StatusCode);
                return Error.Failure("kinescope.videos.batch.failed", "Ошибка пакетного запроса видео");
            }

            KinescopeVideosListResponse? result = await response.Content.ReadFromJsonAsync<KinescopeVideosListResponse>(
                _jsonOptions,
                cancellationToken);

            if (result?.Data is null)
            {
                return new Dictionary<string, VideoProviderAssetInfo>();
            }

            return result.Data.ToDictionary(x => x.Id, MapToInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kinescope batch status request failed");
            return Error.Failure("kinescope.videos.batch.exception", "Внутренняя ошибка при пакетном запросе видео");
        }
    }

    public async Task<Result<IReadOnlyList<VideoProviderChapter>, Error>> GetChaptersAsync(
        string externalAssetId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Result<KinescopeVideoData, Error> videoResult = await GetVideoAsync(externalAssetId, cancellationToken);
            if (videoResult.IsFailure)
                return videoResult.Error;

            return ParseChapters(videoResult.Value.Chapters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kinescope chapters request failed for {ExternalAssetId}", externalAssetId);
            return Error.Failure("kinescope.video.chapters.failed", "Ошибка при получении глав видео");
        }
    }

    public async Task<UnitResult<Error>> ReplaceChaptersAsync(
        string externalAssetId,
        IReadOnlyList<VideoChapterDefinition> chapters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestBody = new
            {
                items = chapters
                    .OrderBy(x => x.StartSeconds)
                    .Select(static chapter => new
                    {
                        time = Convert.ToInt32(Math.Round(Math.Max(0, chapter.StartSeconds) * 1000)),
                        title = chapter.Title,
                    })
                    .ToArray(),
                enabled = chapters.Count > 0,
                show_on_load = chapters.Count > 0,
            };

            using HttpRequestMessage updateRequest = new(
                HttpMethod.Put,
                $"{_options.ApiBaseUrl}/v1/videos/{externalAssetId}/chapters")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions),
            };

            updateRequest.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

            using HttpResponseMessage updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
            if (!updateResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Kinescope chapter update failed with status {StatusCode} for {ExternalAssetId}",
                    updateResponse.StatusCode,
                    externalAssetId);
                return Error.Failure(
                    "kinescope.video.chapters.update.failed",
                    "Ошибка при обновлении глав видео");
            }

            return UnitResult.Success<Error>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kinescope chapter sync failed for {ExternalAssetId}", externalAssetId);
            return Error.Failure("kinescope.video.chapters.exception", "Ошибка при обновлении глав видео");
        }
    }

    private static VideoProviderAssetInfo MapToInfo(KinescopeVideoData data) =>
        new(
            data.Id,
            data.Title,
            data.Status,
            data.Poster?.Md ?? data.Poster?.Sm,
            data.Duration,
            data.Width,
            data.Height);

    private static Result<IReadOnlyList<VideoProviderChapter>, Error> ParseChapters(JsonElement? chaptersElement)
    {
        if (chaptersElement is null || chaptersElement.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Array.Empty<VideoProviderChapter>();

        JsonElement value = chaptersElement.Value;
        if (!value.TryGetProperty("items", out JsonElement itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<VideoProviderChapter>();

        List<VideoProviderChapter> chapters = [];

        int sortOrder = 0;
        foreach (JsonElement item in itemsElement.EnumerateArray())
        {
            string title = item.TryGetProperty("title", out JsonElement titleElement)
                ? titleElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(title))
                continue;

            double startSeconds = item.TryGetProperty("time", out JsonElement timeElement) && timeElement.TryGetDouble(out double timeMilliseconds)
                ? Math.Max(0, timeMilliseconds / 1000d)
                : 0;

            string id = item.TryGetProperty("id", out JsonElement idElement)
                ? idElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(id))
                id = $"chapter-{sortOrder + 1}";

            chapters.Add(new VideoProviderChapter(id, title, startSeconds));
            sortOrder++;
        }

        return chapters
            .OrderBy(x => x.StartSeconds)
            .ToArray();
    }

    private async Task<Result<KinescopeVideoData, Error>> GetVideoAsync(
        string externalAssetId,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{_options.ApiBaseUrl}/v1/videos/{externalAssetId}");
        request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Kinescope video {ExternalAssetId} not found", externalAssetId);
            return Error.NotFound("kinescope.video.not.found", "Видео не найдено");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Kinescope video request failed with status {StatusCode} for {ExternalAssetId}",
                response.StatusCode,
                externalAssetId);
            return Error.Failure("kinescope.video.info.failed", "Ошибка получения информации о видео");
        }

        KinescopeVideoResponse? result = await response.Content.ReadFromJsonAsync<KinescopeVideoResponse>(
            _jsonOptions,
            cancellationToken);

        if (result?.Data is null)
            return Error.Failure("kinescope.video.info.invalid", "Некорректный ответ Kinescope");

        return result.Data;
    }
}