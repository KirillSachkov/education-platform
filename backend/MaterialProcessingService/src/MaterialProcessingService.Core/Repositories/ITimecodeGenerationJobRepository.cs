using System.Linq.Expressions;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Core.Repositories;

public interface ITimecodeGenerationJobRepository
{
    Task AddAsync(TimecodeGenerationJob job, CancellationToken cancellationToken = default);

    Task<TimecodeGenerationJob?> GetActiveByVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken = default);

    Task<TimecodeGenerationJob?> GetByAsync(
        Expression<Func<TimecodeGenerationJob, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<TimecodeGenerationJob, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает batch застрявших job'ов (Status=PROCESSING с UpdatedAt &lt; cutoff)
    ///     для StuckJobSweeper. Используется чтобы помечать их FAILED после OOM/restart,
    ///     иначе partial unique index `ux_timecode_jobs_video_active` блокирует любой
    ///     новый job на том же videoId.
    /// </summary>
    Task<IReadOnlyList<TimecodeGenerationJob>> GetStuckJobsAsync(
        DateTime updatedBefore,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает активные job'ы (QUEUED+PROCESSING) для конкретного юзера.
    ///     Используется глобальным AI-jobs tracker'ом (фронт-вид «что у меня
    ///     в обработке прямо сейчас» через любую страницу).
    /// </summary>
    Task<IReadOnlyList<TimecodeGenerationJob>> GetActiveByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает подмножество <paramref name="videoIds"/>, для которых хотя бы
    ///     один TimecodeGenerationJob завершился со <c>Status=COMPLETED</c>. Используется
    ///     batch-эндпоинтом статусов артефактов (course-builder список модулей).
    /// </summary>
    Task<IReadOnlySet<Guid>> GetVideoIdsWithCompletedTimecodesAsync(
        IReadOnlyCollection<Guid> videoIds,
        Guid? requestedByUserId,
        CancellationToken cancellationToken = default);
}
