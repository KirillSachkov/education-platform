namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Полная информация о материале (объединённый аналог урока и статьи).
/// </summary>
/// <param name="CourseCount">
///     Количество курсов, к которым привязан материал (<c>course_materials</c>).
///     Используется фронтом, чтобы разблокировать опции AccessType=FREE/ENROLLED только при наличии привязок.
/// </param>
/// <param name="QuizId">
///     Квиз «Проверь себя», на который ссылается материал (<c>materials.quiz_id</c>, #489).
///     <c>null</c> — у материала нет квиза. Содержимое квиза фронт тянет отдельно
///     через <c>GET /materials/{id}/quiz</c>.
/// </param>
public sealed record MaterialDetailDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string? Content,
    string? Description,
    string Kind,
    string Status,
    string AccessType,
    bool IsAccessible,
    Guid? ImageId,
    string? ImageUrl,
    Guid? VideoId,
    MaterialVideoDto? Video,
    Guid? QuizId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int CourseCount,
    IReadOnlyList<MaterialChapterDto> Chapters,
    // AuthorDisplayName / AuthorAvatarUrl — авторский кредит материала (#569, model A).
    // Обогащается на бэке через AuthService (display name) + FileService (avatar URL).
    string? AuthorDisplayName = null,
    string? AuthorAvatarUrl = null)
{
    /// <summary>
    ///     Суммарное количество уникальных просмотров: <c>material_views</c> (авторизованные,
    ///     один просмотр на user+material) + <c>anonymous_material_views</c> (анонимы по
    ///     cookie <c>plu_anon_id</c>, один просмотр на cookie+material). Обогащается
    ///     handler'ом <c>GetMaterialDetail</c> через <c>IProgressServiceClient</c>
    ///     (HybridCache 5 min). Issue #234.
    /// </summary>
    public long ViewsCount { get; init; }
}
