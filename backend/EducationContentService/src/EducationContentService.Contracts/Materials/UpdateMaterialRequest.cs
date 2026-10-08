namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Запрос на обновление материала. PUT-семантика — все поля заменяются.
/// </summary>
/// <param name="Description">
///     Авторское описание в Markdown (полезные ссылки, связанные материалы, заметки). Опционально.
///     Отдельно от <paramref name="Content"/> (AI-конспект). <c>null</c>/пусто ⇒ описание очищается.
/// </param>
/// <param name="VideoId">
///     Желаемое состояние привязки видео. <c>null</c> ⇒ открепить текущее (если было),
///     значение ⇒ привязать (idempotent если уже привязано).
/// </param>
/// <param name="PreviewId">Аналогично <paramref name="VideoId"/> для обложки.</param>
/// <param name="QuizId">
///     Желаемое состояние ссылки на квиз «Проверь себя» (#489). <c>null</c> ⇒ отвязать
///     (квиз продолжает жить — он standalone), значение ⇒ привязать (квиз должен
///     существовать и принадлежать caller'у; один квиз может переиспользоваться
///     несколькими материалами).
/// </param>
public sealed record UpdateMaterialRequest(
    string Title,
    string? Content,
    string Kind,
    string AccessType,
    string? Description = null,
    Guid? VideoId = null,
    Guid? PreviewId = null,
    Guid? QuizId = null);
