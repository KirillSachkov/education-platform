using System.Linq.Expressions;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Core.Features.Materials;

/// <summary>
///     Репозиторий для работы с агрегатом <see cref="Material"/>.
///     Объединяет роли <c>ILessonsRepository</c> и <c>IArticlesRepository</c> в рамках
///     рефакторинга Lesson+Article → Material.
/// </summary>
public interface IMaterialsRepository
{
    Task AddAsync(Material material, CancellationToken cancellationToken = default);

    void Delete(Material material);

    Task<Result<Material, Error>> GetByAsync(
        Expression<Func<Material, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<Material, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Material>> GetManyByAsync(
        Expression<Func<Material, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает минимальную access-проекцию материалов для batch-проверки ссылок.
    ///     Несуществующие идентификаторы отсутствуют в результате.
    /// </summary>
    Task<IReadOnlyList<MaterialReferenceAccess>> GetReferenceAccessBatchAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает все материалы, у которых <see cref="Material.VideoId"/> равен
    ///     указанному. Отдельный метод вместо expression-based <see cref="GetManyByAsync"/>
    ///     потому что EF ValueConverter на VideoId VO ломает translation
    ///     (`m.VideoId.Value == ...` not translatable, `m.VideoId == VO` бьёт
    ///     ChangeType(Guid, VideoId) на parameter sanitization). Реализация через
    ///     FromSqlInterpolated обходит converter — Npgsql параметризует Guid
    ///     напрямую, а converter применяется только при гидрации в Material.
    /// </summary>
    Task<IReadOnlyList<Material>> GetManyByVideoIdAsync(
        Guid videoId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Уникальность заголовка среди non-DRAFT материалов (PUBLISHED + ARCHIVED).
    ///     Бизнес-правило вынесено в репозиторий — см. INV-5.
    /// </summary>
    Task<bool> ExistsByTitleAsync(Title title, Guid? excludeId, CancellationToken cancellationToken = default);

    /// <summary>Список курсов, к которым привязан материал через modules.</summary>
    Task<List<Guid>> GetCourseIdsAsync(Guid materialId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Батч-версия <see cref="GetCourseIdsAsync"/> для bulk-операций — одним запросом
    ///     возвращает mapping <c>materialId → [courseId, …]</c>. Orphan-материалы
    ///     присутствуют в словаре с пустым списком.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetCourseIdsBatchAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default);

    /// <summary>Автор первого курса, к которому привязан материал (или null).</summary>
    Task<Guid?> GetCourseAuthorIdAsync(Guid materialId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     True, если материал привязан хотя бы к одному курсу, которым владеет
    ///     <paramref name="userId"/>. Питает course-aware ownership: владелец курса
    ///     управляет материалами в своём курсе, даже если их автор — кто-то другой
    ///     (например, админ, добавивший материал в чужой курс). См. #657.
    /// </summary>
    Task<bool> IsMaterialInOwnedCourseAsync(
        Guid materialId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Список модулей, к которым привязан материал.
    ///     В отличие от <c>ILessonsRepository.GetModuleIdAsync</c>, материал
    ///     может входить в несколько модулей.
    /// </summary>
    Task<List<Guid>> GetModuleIdsAsync(Guid materialId, CancellationToken cancellationToken = default);
}

public sealed record MaterialReferenceAccess(
    Guid Id,
    Guid AuthorId,
    PublicationStatus Status,
    AccessType AccessType);
