namespace MaterialProcessingService.Contracts.Timecodes.Dtos;

/// <summary>
///     Batch-запрос статусов артефактов (transcript / timecodes / summary) для набора
///     видео. Используется автор-фронтом курс-конструктора, чтобы рисовать иконки
///     «есть/нет» рядом с видео-материалами без поллинга single-endpoint'а на каждый.
/// </summary>
/// <param name="Items">
///     Пары (videoId, materialId). MaterialId нужен для resolve'а конспекта
///     (<see cref="VideoArtifactStatusDto.HasSummary"/>) — он scoped по материалу,
///     не по видео. Если у item только videoId, поле HasSummary всегда false.
/// </param>
public sealed record GetVideoArtifactStatusesRequest(IReadOnlyList<VideoArtifactQuery> Items);

/// <summary>
///     Single запрос статусов для одного видео.
/// </summary>
public sealed record VideoArtifactQuery(Guid VideoId, Guid? MaterialId);
