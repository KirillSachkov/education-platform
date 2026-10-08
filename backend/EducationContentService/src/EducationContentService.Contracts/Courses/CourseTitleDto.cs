namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Минимальный батч-проекшн курса: id + title (+ slug/kind для линкабельных
///     карточек). Используется ProgressService'ом для enrichment'а review-feed'а
///     (название вместо GUID) и AccessService'ом для блока «включённые курсы» плана
///     (bundle, #404) — там нужны <see cref="Slug"/> (ссылка на курс) и
///     <see cref="Kind"/> (бейдж COURSE/INTENSIVE/MARATHON). <c>Slug</c>/<c>Kind</c> —
///     с дефолтами, чтобы старые конструкторы (id+title) не ломались; Dapper заполняет
///     их по имени колонки.
/// </summary>
public sealed record CourseTitleDto(Guid CourseId, string Title, string Slug = "", string Kind = "COURSE");

public sealed record GetCourseTitlesRequest(IReadOnlyCollection<Guid> Ids);
