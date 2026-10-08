using System.Linq.Expressions;
using Dapper;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class MaterialsRepository : IMaterialsRepository
{
    private readonly EducationDbContext _dbContext;

    public MaterialsRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Material material, CancellationToken cancellationToken = default)
    {
        await _dbContext.Materials.AddAsync(material, cancellationToken);
    }

    public void Delete(Material material)
    {
        _dbContext.Materials.Remove(material);
    }

    public async Task<Result<Material, Error>> GetByAsync(
        Expression<Func<Material, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Material? material = await _dbContext.Materials.FirstOrDefaultAsync(predicate, cancellationToken);

        return material is null
            ? GeneralErrors.NotFound()
            : material;
    }

    public Task<bool> ExistsAsync(
        Expression<Func<Material, bool>> predicate,
        CancellationToken cancellationToken = default)
        => _dbContext.Materials.AnyAsync(predicate, cancellationToken);

    public async Task<IReadOnlyList<Material>> GetManyByAsync(
        Expression<Func<Material, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await _dbContext.Materials.Where(predicate).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MaterialReferenceAccess>> GetReferenceAccessBatchAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default)
    {
        if (materialIds.Count == 0)
            return [];

        Guid[] ids = materialIds.ToArray();
        return await _dbContext.Materials
            .AsNoTracking()
            .Where(material => ids.Contains(material.Id))
            .Select(material => new MaterialReferenceAccess(
                material.Id,
                material.AuthorId,
                material.Status,
                material.AccessType))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Material>> GetManyByVideoIdAsync(
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        // EF ValueConverter на VideoId блокирует expression-фильтр по полю.
        // Резолвим IDs через Dapper raw SQL → грузим через EF (tracked, чтобы
        // handler мог мутировать ChapterTitles/Timestamps и SaveChangesAsync
        // эмитнул UPDATE).
        const string idsSql = "SELECT id FROM education.materials WHERE video_id = @VideoId";
        System.Data.Common.DbConnection connection = _dbContext.Database.GetDbConnection();
        IEnumerable<Guid> idsResult = await connection.QueryAsync<Guid>(
            new CommandDefinition(idsSql, new { VideoId = videoId }, cancellationToken: cancellationToken));
        Guid[] ids = idsResult.ToArray();
        if (ids.Length == 0)
            return Array.Empty<Material>();

        return await _dbContext.Materials
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsByTitleAsync(Title title, Guid? excludeId, CancellationToken cancellationToken = default)
        => _dbContext.Materials
            .AnyAsync(
                // INV-5: уникальность Title глобально (не per-AuthorId — индекс ix_materials_title без AuthorId)
                // среди non-DRAFT (PUBLISHED + ARCHIVED). Раньше код проверял только PUBLISHED, что
                // приводило к UniqueConstraintViolation вместо доменной ошибки при коллизии с ARCHIVED.
                m => m.Title == title
                     && (m.Status == PublicationStatus.PUBLISHED || m.Status == PublicationStatus.ARCHIVED)
                     && (excludeId == null || m.Id != excludeId),
                cancellationToken);

    public async Task<List<Guid>> GetCourseIdsAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        // После унификации (MATERIAL_LIFECYCLE.md INV-4): course_materials — единственный источник
        // правды о привязке материала к курсу. UNION с module_items больше не нужен.
        const string sql = """
            SELECT cm.course_id
            FROM course_materials cm
            WHERE cm.material_id = @MaterialId
            """;

        var connection = _dbContext.Database.GetDbConnection();
        var result = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { MaterialId = materialId }, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetCourseIdsBatchAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default)
    {
        if (materialIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Guid>>();

        const string sql = """
            SELECT cm.material_id AS MaterialId, cm.course_id AS CourseId
            FROM course_materials cm
            WHERE cm.material_id = ANY(@MaterialIds)
            """;

        System.Data.Common.DbConnection connection = _dbContext.Database.GetDbConnection();
        IEnumerable<(Guid MaterialId, Guid CourseId)> rows = await connection.QueryAsync<(Guid MaterialId, Guid CourseId)>(
            new CommandDefinition(sql, new { MaterialIds = materialIds.ToArray() }, cancellationToken: cancellationToken));

        Dictionary<Guid, IReadOnlyList<Guid>> result = rows
            .GroupBy(r => r.MaterialId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.CourseId).ToList());

        // Ensure every requested materialId has an entry (orphan materials → empty list).
        foreach (Guid id in materialIds)
        {
            if (!result.ContainsKey(id))
                result[id] = Array.Empty<Guid>();
        }

        return result;
    }

    public async Task<Guid?> GetCourseAuthorIdAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        // Унифицировано: путь через course_materials. Раньше шли через module_items+course_items —
        // не находил материалы привязанные напрямую (без модуля). После INV-4 module_items всегда
        // сопровождается course_materials, так что прямой путь покрывает все случаи.
        const string sql = """
            SELECT c.author_id
            FROM course_materials cm
            JOIN courses c ON c.id = cm.course_id
            WHERE cm.material_id = @MaterialId
            LIMIT 1
            """;

        var connection = _dbContext.Database.GetDbConnection();
        return await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(sql, new { MaterialId = materialId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> IsMaterialInOwnedCourseAsync(
        Guid materialId, Guid userId, CancellationToken cancellationToken = default)
    {
        // course-aware ownership (#657): владелец курса правит материалы в своём курсе,
        // даже если их автор — другой (админ добавил материал в чужой курс). course_materials —
        // единственный источник правды о привязке (INV-4), поэтому module_items не нужен.
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM course_materials cm
                JOIN courses c ON c.id = cm.course_id
                WHERE cm.material_id = @MaterialId AND c.author_id = @UserId
            )
            """;

        var connection = _dbContext.Database.GetDbConnection();
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { MaterialId = materialId, UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<List<Guid>> GetModuleIdsAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT mi.module_id
            FROM module_items mi
            WHERE mi.reference_id = @MaterialId AND mi.item_type = 'Material'
            """;

        var connection = _dbContext.Database.GetDbConnection();
        var result = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { MaterialId = materialId }, cancellationToken: cancellationToken));
        return result.ToList();
    }
}
