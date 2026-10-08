using System.Linq.Expressions;
using Dapper;
using EducationContentService.Core.Features.ModuleItems;
using EducationContentService.Domain.Modules;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class ModulesRepository : IModulesRepository
{
    private readonly EducationDbContext _dbContext;

    public ModulesRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Module module, CancellationToken cancellationToken = default)
    {
        await _dbContext.Modules.AddAsync(module, cancellationToken);
    }

    public async Task<Result<Module, Error>> GetByAsync(
        Expression<Func<Module, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Module? module = await _dbContext.Modules.FirstOrDefaultAsync(predicate, cancellationToken);

        return module is null
            ? GeneralErrors.NotFound()
            : module;
    }

    public async Task<Guid?> GetCourseIdAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT ci.course_id
            FROM course_items ci
            WHERE ci.reference_id = @ModuleId AND ci.item_type = 'Module'
            LIMIT 1
            """;

        var connection = _dbContext.Database.GetDbConnection();
        return await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(sql, new { ModuleId = moduleId }, cancellationToken: cancellationToken));
    }
}
