using System.Linq.Expressions;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Users;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class ProgressUserRepository : IProgressUserRepository
{
    private readonly ProgressDbContext _dbContext;

    public ProgressUserRepository(ProgressDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ProgressUser user, CancellationToken cancellationToken = default)
    {
        await _dbContext.ProgressUsers.AddAsync(user, cancellationToken);
    }

    public async Task<Result<ProgressUser, Error>> GetByAsync(
        Expression<Func<ProgressUser, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ProgressUser? user = await _dbContext.ProgressUsers
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (user is null)
        {
            return Error.NotFound("progress.user.not.found", "Пользователь не найден");
        }

        return Result.Success<ProgressUser, Error>(user);
    }
}
