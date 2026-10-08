using EducationContentService.Core.Features.ModuleItems;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public sealed class ModuleItemsRepository : OrderedItemsRepository<ModuleItem>, IModuleItemsRepository
{
    private readonly EducationDbContext _dbContext;

    public ModuleItemsRepository(EducationDbContext dbContext)
        : base(dbContext, "Module") => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<UnitResult<Error>> CheckIssueNotAttachedAsync(
        Guid issueId, CancellationToken cancellationToken = default)
    {
        bool exists = await _dbContext.ModuleItems
            .AnyAsync(x => x.ReferenceId == issueId && x.ItemType == ModuleItemType.Issue, cancellationToken);

        return exists
            ? EducationErrors.IssueAlreadyInModule(issueId)
            : UnitResult.Success<Error>();
    }

    /// <inheritdoc />
    public async Task<bool> HasOtherQuizItemsInCourseAsync(
        Guid courseId, Guid quizId, Guid excludedItemId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ModuleItems
            .Where(mi => mi.ItemType == ModuleItemType.Quiz
                         && mi.ReferenceId == quizId
                         && mi.Id != excludedItemId)
            .Join(
                _dbContext.CourseItems.Where(ci =>
                    ci.CourseId == courseId && ci.ItemType == CourseItemType.Module),
                mi => mi.ModuleId,
                ci => ci.ReferenceId,
                (mi, ci) => mi.Id)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasIssueItemsInCourseAsync(
        Guid courseId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ModuleItems
            .Where(mi => mi.ItemType == ModuleItemType.Issue)
            .Join(
                _dbContext.CourseItems.Where(ci =>
                    ci.CourseId == courseId && ci.ItemType == CourseItemType.Module),
                mi => mi.ModuleId,
                ci => ci.ReferenceId,
                (mi, ci) => mi.Id)
            .AnyAsync(cancellationToken);
    }
}
