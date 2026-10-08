namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Запрос на создание draft-материала. Все поля опциональны — handler ставит
///     placeholder title и default kind=ARTICLE. Используется для YouTube-style flow,
///     где material entity создаётся сразу при открытии формы, а правки идут уже
///     на реальный id (refresh не теряет прогресс).
/// </summary>
/// <param name="CourseId">
///     Опциональная привязка к курсу — материал сразу попадает в <c>course_materials</c>.
///     Требует ownership на курс.
/// </param>
/// <param name="ModuleId">
///     Опциональная привязка к модулю — материал попадает в <c>module_items</c>
///     с <c>ViewPriority=Key</c>. Если CourseId не задан, выводится из модуля.
/// </param>
/// <param name="CollectionId">
///     Опциональная привязка к подборке. Если задан вместе с <paramref name="SectionId"/>,
///     материал атомарно добавляется в <c>collection_items</c> в той же транзакции, что
///     и создание самого материала. Требует ownership на подборку.
/// </param>
/// <param name="SectionId">
///     Секция подборки. Должна принадлежать <paramref name="CollectionId"/>. Оба поля
///     либо <c>null</c>, либо оба заданы.
/// </param>
public sealed record CreateDraftMaterialRequest(
    Guid? CourseId = null,
    Guid? ModuleId = null,
    Guid? CollectionId = null,
    Guid? SectionId = null);
