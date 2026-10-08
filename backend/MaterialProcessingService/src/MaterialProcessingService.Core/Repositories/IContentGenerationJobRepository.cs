using System.Linq.Expressions;
using MaterialProcessingService.Domain.ContentDrafts;

namespace MaterialProcessingService.Core.Repositories;

public interface IContentGenerationJobRepository
{
    Task AddAsync(ContentGenerationJob job, CancellationToken cancellationToken = default);

    Task<ContentGenerationJob?> GetActiveByVideoAndMaterialAsync(
        Guid videoId,
        Guid materialId,
        CancellationToken cancellationToken = default);

    Task<ContentGenerationJob?> GetByAsync(
        Expression<Func<ContentGenerationJob, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает batch застрявших job'ов (Status=PROCESSING с UpdatedAt &lt; cutoff)
    ///     для StuckJobSweeper. Без него после pod-kill в середине AI-вызова job висит
    ///     в PROCESSING, partial unique index блокирует новый content-draft на том же
    ///     (videoId, materialId), автор уходит в тупик.
    /// </summary>
    Task<IReadOnlyList<ContentGenerationJob>> GetStuckJobsAsync(
        DateTime updatedBefore,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает активные content-job'ы (QUEUED+PROCESSING) для конкретного юзера.
    ///     Используется глобальным AI-jobs tracker'ом — собирает union с timecode-job'ами.
    /// </summary>
    Task<IReadOnlyList<ContentGenerationJob>> GetActiveByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает подмножество <paramref name="materialIds"/>, для которых хотя бы один
    ///     ContentGenerationJob завершился со <c>Status=COMPLETED</c>. Конспект scoped по
    ///     материалу (не по видео), поэтому ключ — MaterialId.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetMaterialIdsWithCompletedSummaryAsync(
        IReadOnlyCollection<Guid> materialIds,
        Guid? requestedByUserId,
        CancellationToken cancellationToken = default);
}
