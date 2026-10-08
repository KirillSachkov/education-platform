using EducationContentService.Domain.Courses;
using Ordering;

namespace EducationContentService.Core.Features.CourseItems;

public interface ICourseItemsRepository : IOrderedItemsRepository<CourseItem>;
