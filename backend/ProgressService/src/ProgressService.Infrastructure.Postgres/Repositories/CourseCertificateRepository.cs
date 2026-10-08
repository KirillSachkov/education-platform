using System.Linq.Expressions;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Certificates;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class CourseCertificateRepository : ICourseCertificateRepository
{
    private readonly ProgressDbContext _dbContext;

    public CourseCertificateRepository(ProgressDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(CourseCertificate certificate, CancellationToken cancellationToken = default)
    {
        await _dbContext.CourseCertificates.AddAsync(certificate, cancellationToken);
    }

    public Task<CourseCertificate?> GetByAsync(
        Expression<Func<CourseCertificate, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.CourseCertificates.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public async Task<IReadOnlyList<CourseCertificate>> GetManyByAsync(
        Expression<Func<CourseCertificate, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.CourseCertificates
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }
}
