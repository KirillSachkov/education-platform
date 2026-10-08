namespace EducationContentService.Contracts.ProgressLookup;

/// <summary>
///     Контекст материала в конкретном курсе: (CourseId, ModuleId, ModuleItemsTotal).
///     Используется ProgressService для cascade'а просмотра материала на все курсы,
///     где он присутствует: обновляет <c>module_item_progress</c> в каждом enrollment'е.
///     <c>ModuleItemsTotal</c> нужен для ленивого создания <c>module_progress</c> при
///     первом просмотре материала в этом модуле.
/// </summary>
public sealed record MaterialCourseContextDto(Guid CourseId, Guid ModuleId, int ModuleItemsTotal);
