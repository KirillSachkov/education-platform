namespace EducationContentService.Domain.Courses;

/// <summary>
///     Тип элемента, прикреплённого к курсу через <see cref="CourseItem"/>.
/// </summary>
public enum CourseItemType
{
    /// <summary>Теоретический модуль.</summary>
    Module,

    /// <summary>Практический проект.</summary>
    Project
}
