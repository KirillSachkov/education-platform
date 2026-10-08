namespace EducationContentService.Contracts.ProgressLookup;

/// <summary>
///     Контекст квиза в конкретном курсе: (CourseId, ModuleId, ModuleItemsTotal) —
///     зеркало <see cref="MaterialCourseContextDto"/> для <c>module_items(item_type='Quiz')</c>.
///     Используется ProgressService для cascade'а passed-попытки квиза на
///     <c>module_item_progress</c> во всех курсах, где квиз размещён (ST-13 #493).
///     <c>ModuleItemsTotal</c> нужен для ленивого создания <c>module_progress</c> при
///     первом прохождении квиза в этом модуле.
/// </summary>
public sealed record QuizModuleContextDto(Guid CourseId, Guid ModuleId, int ModuleItemsTotal);
