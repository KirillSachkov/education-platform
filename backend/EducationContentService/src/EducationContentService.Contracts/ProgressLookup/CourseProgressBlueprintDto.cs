namespace EducationContentService.Contracts.ProgressLookup;

/// <summary>
///     Легковесный DTO прогресс-блюпринта курса для сервиса прогресса (service-to-service).
///     <para>
///         <c>MaterialIds</c> — distinct material id'шники, считающиеся материалами этого курса:
///         union <c>course_materials</c> (включает модульные материалы по INV-4 + Лента курса)
///         и <c>collection_items</c> опубликованных подборок курса. Дедуп по material_id.
///     </para>
///     <para>
///         <c>QuizIds</c> — distinct PUBLISHED-квизы из <c>module_items(item_type='Quiz')</c>
///         модулей курса (ST-13 #493). Прогресс считается по элементам программы, поэтому
///         квиз попадает в blueprint только будучи размещённым в модуле (запись в
///         <c>course_quizzes</c> без module_item — привязка без позиции в программе).
///         <c>TotalItems</c> = TotalMaterials + TotalUniqueIssues + TotalQuizzes.
///     </para>
/// </summary>
public sealed record CourseProgressBlueprintDto(
    Guid CourseId,
    string CourseSlug,
    string Title,
    string Description,
    Guid? ImageId,
    string? ImageUrl,
    int TotalModules,
    int TotalMaterials,
    IReadOnlyList<Guid> MaterialIds,
    int TotalUniqueIssues,
    int TotalQuizzes,
    IReadOnlyList<Guid> QuizIds,
    int TotalItems,
    bool IsNew,
    string SortKey,
    string Kind);
