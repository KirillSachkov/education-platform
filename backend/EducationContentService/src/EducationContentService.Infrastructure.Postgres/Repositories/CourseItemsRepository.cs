using EducationContentService.Core.Features.CourseItems;
using EducationContentService.Domain.Courses;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public sealed class CourseItemsRepository(EducationDbContext dbContext)
    : OrderedItemsRepository<CourseItem>(dbContext, "Course"), ICourseItemsRepository;
