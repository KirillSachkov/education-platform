namespace MaterialProcessingService.Contracts.Timecodes.Dtos;

public sealed record GetVideoArtifactStatusesResponse(IReadOnlyList<VideoArtifactStatusDto> Items);

/// <summary>
///     Фактическое состояние видео-артефактов:
///     - <see cref="HasTranscript"/> — exists хотя бы один <c>VideoTranscript</c> для VideoAssetId
///       (любая AssetVersion). Used as «можно зайти и почитать», не как «соответствует
///       текущей версии видео».
///     - <see cref="HasTimecodes"/> — exists <c>TimecodeGenerationJob</c> со <c>Status=COMPLETED</c>
///       для VideoAssetId.
///     - <see cref="HasSummary"/> — exists <c>ContentGenerationJob</c> со <c>Status=COMPLETED</c>
///       для MaterialId. Если MaterialId не передан в запросе — всегда false.
/// </summary>
public sealed record VideoArtifactStatusDto(
    Guid VideoId,
    Guid? MaterialId,
    bool HasTranscript,
    bool HasTimecodes,
    bool HasSummary);
