namespace MaterialProcessingService.Core.Cleanup;

/// <summary>
///     Bulk-cleanup orphan'ных данных, накопленных pipeline'ом обработки видео,
///     когда либо видео-asset удалён в FileService, либо материал hard-delete'нут в ECS.
///     Все операции идемпотентны — повторный вызов на отсутствующих данных no-op.
/// </summary>
public interface IMaterialProcessingCleanup
{
    /// <summary>
    ///     Удаляет всё, что относится к видео-asset'у: транскрипт, таймкод-job'ы и
    ///     content-генерации, где этот video — источник.
    /// </summary>
    Task<int> DeleteByVideoAssetIdAsync(Guid videoAssetId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет content-генерации, ссылающиеся на удалённый материал. Транскрипт
    ///     и timecode-job'ы остаются (живут на video-asset'е, не на material'е).
    /// </summary>
    Task<int> DeleteContentJobsByMaterialIdAsync(Guid materialId, CancellationToken cancellationToken = default);
}
