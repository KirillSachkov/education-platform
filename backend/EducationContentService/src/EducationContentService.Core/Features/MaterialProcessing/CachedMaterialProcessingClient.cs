using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MaterialProcessingService.Contracts.HttpCommunication;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using Microsoft.Extensions.Caching.Hybrid;

namespace EducationContentService.Core.Features.MaterialProcessing;

/// <summary>
///     Decorator over <see cref="IMaterialProcessingServiceClient"/> for course-builder reads.
///     Кэширует только batch-эндпоинт <c>GetArtifactStatusesAsync</c> — он зовётся каждый
///     раз при открытии страницы преподавания, а изменения наступают только когда автор
///     запускает обработку (минуты-десятки минут). Single GET-эндпоинт <c>GetVideoTimecodes</c>
///     поллится каждые 2-3 секунды, кэш только мешал бы — pass-through.
///
///     <para>
///     Стратегия — кэшируем <b>весь ответ batch'а целиком</b> по ключу из набора
///     <c>(videoId, materialId)</c> пар. Per-item кэш с factory-trick'ом не работает для
///     «всё false» (наиболее частый кейс — у только что загруженных видео артефактов нет):
///     HybridCache хранит null factory-результаты, и каждый последующий request попадает
///     в else-ветку как cache miss. Whole-request паттерн проще и стабильнее: один и тот
///     же набор items стабилен между перезагрузками страницы → стабильный hit.
///     </para>
///
///     <para>
///     Invalidation: TTL-only (2 мин). Нет integration events с MPS на переход job'а в
///     COMPLETED, поэтому полагаемся на короткий TTL. После запуска новой обработки автор
///     всё равно перейдёт на страницу материала, где single-endpoint показывает живой статус.
///     </para>
/// </summary>
internal sealed class CachedMaterialProcessingClient : IMaterialProcessingServiceClient
{
    internal const string ARTIFACT_BATCH_CACHE_KEY_PREFIX = "material-processing:artifacts-batch:";

    private readonly IMaterialProcessingServiceClient _inner;
    private readonly HybridCache _cache;

    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(2),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    public CachedMaterialProcessingClient(IMaterialProcessingServiceClient inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<Result<GetVideoTimecodesResponse, Error>> GetVideoTimecodesAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        _inner.GetVideoTimecodesAsync(videoId, cancellationToken);

    public async Task<Result<GetVideoArtifactStatusesResponse, Error>> GetArtifactStatusesAsync(
        GetVideoArtifactStatusesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            return new GetVideoArtifactStatusesResponse([]);

        string key = BuildBatchCacheKey(request);
        Error? fetchError = null;

        GetVideoArtifactStatusesResponse? cached = await _cache.GetOrCreateAsync(
            key,
            async ct =>
            {
                Result<GetVideoArtifactStatusesResponse, Error> result =
                    await _inner.GetArtifactStatusesAsync(request, ct);
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

        return cached ?? new GetVideoArtifactStatusesResponse([]);
    }

    /// <summary>
    ///     Стабильный hash от sorted (videoId, materialId) пар. Тот же набор → тот же ключ
    ///     независимо от порядка items в request'е и кейса материала без attachemnt'а.
    /// </summary>
    private static string BuildBatchCacheKey(GetVideoArtifactStatusesRequest request)
    {
        IOrderedEnumerable<VideoArtifactQuery> sorted = request.Items
            .OrderBy(x => x.VideoId)
            .ThenBy(x => x.MaterialId ?? Guid.Empty);

        var sb = new StringBuilder(request.Items.Count * 75);
        foreach (VideoArtifactQuery item in sorted)
        {
            sb.Append(item.VideoId.ToString("N", CultureInfo.InvariantCulture));
            sb.Append('_');
            sb.Append(item.MaterialId?.ToString("N", CultureInfo.InvariantCulture) ?? "_");
            sb.Append(',');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"{ARTIFACT_BATCH_CACHE_KEY_PREFIX}{Convert.ToHexString(hash)}";
    }
}
