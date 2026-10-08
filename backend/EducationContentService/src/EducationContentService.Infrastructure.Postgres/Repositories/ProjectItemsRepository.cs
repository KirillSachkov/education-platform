using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Domain.Projects;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public sealed class ProjectItemsRepository(EducationDbContext dbContext)
    : OrderedItemsRepository<ProjectItem>(dbContext, "Project"), IProjectItemsRepository;
