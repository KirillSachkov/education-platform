using System.Linq.Expressions;
using MaterialProcessingService.Domain.Transcripts;

namespace MaterialProcessingService.Core.Repositories;

public interface IVideoTranscriptRepository
{
    Task AddAsync(VideoTranscript transcript, CancellationToken cancellationToken = default);

    Task<VideoTranscript?> GetByVideoAssetVersionAsync(
        Guid videoAssetId,
        Guid assetVersion,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    Task<VideoTranscript?> GetByAsync(
        Expression<Func<VideoTranscript, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет cached transcript для пары (videoAssetId, assetVersion). Используется
    ///     для force-regeneration: handler delete'ит транскрипт до запуска pipeline,
    ///     чтобы `GetByVideoAssetVersionAsync` не вернул кэш.
    /// </summary>
    Task<int> DeleteByVideoAssetVersionAsync(
        Guid videoAssetId,
        Guid assetVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает подмножество <paramref name="videoIds"/>, для которых сохранён
    ///     хотя бы один <c>VideoTranscript</c> (любая AssetVersion). Для UX-бейджа
    ///     достаточно «есть транскрипт» — сверка с текущей версией видео делается
    ///     уже при заходе на страницу материала.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetVideoIdsWithTranscriptAsync(
        IReadOnlyCollection<Guid> videoIds,
        Guid? requestedByUserId,
        CancellationToken cancellationToken = default);
}
