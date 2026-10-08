using EducationContentService.Domain.Projects;
using Ordering;

namespace EducationContentService.Core.Features.ProjectItems;

public interface IProjectItemsRepository : IOrderedItemsRepository<ProjectItem>;
