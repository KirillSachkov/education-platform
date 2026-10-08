using System.Linq.Expressions;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public sealed class ReviewConfigRepository : IReviewConfigRepository
{
    private readonly EducationDbContext _db;

    public ReviewConfigRepository(EducationDbContext db) => _db = db;

    public async Task AddProjectReviewContextAsync(ProjectReviewContext context, CancellationToken ct = default)
    {
        await _db.ProjectReviewContexts.AddAsync(context, ct);
    }

    public Task<ProjectReviewContext?> GetProjectReviewContextAsync(
        Expression<Func<ProjectReviewContext, bool>> predicate,
        CancellationToken ct = default) =>
        _db.ProjectReviewContexts.FirstOrDefaultAsync(predicate, ct);

    public async Task AddReviewSpecAsync(ReviewSpec spec, CancellationToken ct = default)
    {
        await _db.ReviewSpecs.AddAsync(spec, ct);
    }

    public Task<ReviewSpec?> GetReviewSpecAsync(
        Expression<Func<ReviewSpec, bool>> predicate,
        CancellationToken ct = default) =>
        _db.ReviewSpecs.FirstOrDefaultAsync(predicate, ct);
}
