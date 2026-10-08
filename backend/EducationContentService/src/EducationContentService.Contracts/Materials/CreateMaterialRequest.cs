namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Запрос на создание материала.
/// </summary>
/// <param name="Title">Заголовок.</param>
/// <param name="Content">Markdown-контент (опционально).</param>
/// <param name="Description">
///     Авторское описание в Markdown (полезные ссылки, связанные материалы, заметки). Опционально.
///     Отдельно от <paramref name="Content"/> (AI-конспект) — редактируется автором вручную.
/// </param>
/// <param name="Kind">Тип материала (ARTICLE | VIDEO | NOTE | STREAM).</param>
/// <param name="AccessType">Уровень доступа (PUBLIC | REGISTERED | FREE | ENROLLED).</param>
/// <param name="DraftId">
///     Опциональный draft id для batch-биндинга markdown-ассетов из контента после создания
///     (через async <c>BindMaterialDraftAssets</c> event → FileService). Не используется
///     для <paramref name="VideoId"/>/<paramref name="PreviewId"/> — те биндятся синхронно.
/// </param>
/// <param name="VideoId">
///     Опциональный id уже загруженного видео-ассета. ECS делает sync-привязку к материалу
///     перед сохранением; при ошибке — материал не создаётся.
/// </param>
/// <param name="PreviewId">
///     Опциональный id уже загруженной обложки. Поведение аналогично <paramref name="VideoId"/>.
/// </param>
/// <param name="CourseId">
///     Опциональный id курса — если задан, в той же транзакции создаётся привязка
///     <c>course_materials(CourseId, MaterialId)</c>. Нужно для AccessType=FREE/ENROLLED
///     (иначе нарушается инвариант INV-3), а также для UX «создать материал в контексте курса».
/// </param>
/// <param name="ModuleId">
///     Опциональный id модуля — если задан, в той же транзакции создаётся элемент
///     <c>module_items</c> типа <c>Material</c>. Требует, чтобы <paramref name="CourseId"/> соответствовал
///     курсу этого модуля (либо был пуст — в этом случае courseId подтягивается из модуля).
/// </param>
/// <param name="PublishOnCreate">
///     Если <c>true</c>, в той же транзакции материал переводится в <c>PUBLISHED</c> и публикуется
///     <c>MaterialPublished</c> integration event. Требует наличия <paramref name="Content"/> или
///     <paramref name="VideoId"/> (инвариант <c>Material.Publish()</c>).
/// </param>
/// <param name="NotifySubscribers">
///     Применяется только при <see cref="PublishOnCreate"/>=<c>true</c>. Если <c>true</c> —
///     подписчики курса получат уведомление о новом материале (InApp + Telegram). Если <c>false</c> —
///     материал публикуется тихо. Игнорируется при создании в DRAFT.
/// </param>
/// <param name="AuthorId">
///     Опциональный override автора. Применяется ТОЛЬКО для admin-вызовов (например, MCP
///     client_credentials seed-сценариев). Для не-admin caller'а игнорируется и author берётся
///     из <c>UserScopedData.UserId</c>. Нужен потому, что у service-токенов sub=client_id
///     (не UUID) → <c>UserId = Guid.Empty</c>; без override материал ушёл бы под пустого автора
///     и сорвал бы Redis-теги доступа (см. <c>ContentAccessTagBuilder</c>).
/// </param>
/// <param name="QuizId">
///     Опциональная ссылка на квиз «Проверь себя» (#489). Квиз должен существовать
///     и принадлежать caller'у; один квиз может переиспользоваться несколькими материалами.
/// </param>
public sealed record CreateMaterialRequest(
    string Title,
    string? Content,
    string Kind,
    string AccessType,
    string? Description = null,
    string? DraftId = null,
    Guid? VideoId = null,
    Guid? PreviewId = null,
    Guid? CourseId = null,
    Guid? ModuleId = null,
    bool PublishOnCreate = false,
    bool NotifySubscribers = true,
    Guid? AuthorId = null,
    Guid? QuizId = null);
