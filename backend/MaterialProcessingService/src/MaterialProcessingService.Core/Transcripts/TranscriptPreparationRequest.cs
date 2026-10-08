namespace MaterialProcessingService.Core.Transcripts;

public sealed record TranscriptPreparationRequest(
    Guid VideoAssetId,
    Guid AssetVersion,
    Guid ProcessingId,
    string EmptyTranscriptErrorCode,
    /// <summary>
    ///     Optional STT model override (admin-only). Влияет только если кэша
    ///     транскрипта по AssetVersion ещё нет — иначе вернётся cached.
    /// </summary>
    string? SttModelOverride = null);
