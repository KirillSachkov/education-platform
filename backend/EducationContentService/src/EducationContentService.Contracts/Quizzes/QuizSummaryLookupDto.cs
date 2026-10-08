namespace EducationContentService.Contracts.Quizzes;

/// <summary>
///     Минимальный S2S батч-проекшн квиза: id + title + purpose + представительный
///     курс. Используется ProgressService'ом для enrichment'а страницы «Мои тесты»
///     (название/назначение вместо GUID) и админ-статистикой по тестам (#556).
///     <see cref="CourseId"/> — один из курсов, в которых квиз размещён
///     (<c>MIN(course_id)</c> из <c>course_quizzes</c>); <c>null</c> для standalone-квиза
///     без курсовой привязки. <see cref="Purpose"/> — <c>MATERIAL_CHECK | LEVEL_TEST</c>;
///     consumer фильтрует LEVEL_TEST (у воронки своя страница). Отсутствующие в БД ID
///     (квиз hard-deleted) просто не попадают в ответ.
/// </summary>
public sealed record QuizSummaryLookupDto(Guid Id, string Title, string Purpose, Guid? CourseId);

public sealed record GetQuizSummariesRequest(IReadOnlyCollection<Guid> Ids);
