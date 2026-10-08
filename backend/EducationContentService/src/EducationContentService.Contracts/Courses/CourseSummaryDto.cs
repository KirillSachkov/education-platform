namespace EducationContentService.Contracts.Courses;

public sealed record CourseSummaryDto(
    Guid Id,
    Guid AuthorId,
    string Slug,
    string Title,
    string Description,
    string Status,
    string Kind,
    Guid? ImageId,
    Guid? VideoId,
    bool IsNew,
    string SortKey,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // ShowInFullAccess — display-only флаг: показывать ли курс в showcase «Полный доступ» на /pricing.
    // На доступ не влияет (см. Course.ShowInFullAccess). Default true для обратной совместимости.
    bool ShowInFullAccess = true,
    // IsCatalogListed — одобрен ли курс к показу в публичном каталоге (#569, model A).
    // Автор видит свой курс в /courses/my независимо от флага; false ⇒ UI рисует
    // бейдж «на модерации витрины». Default true для обратной совместимости.
    bool IsCatalogListed = true,
    // Author credit (#637): отображаемое имя + URL аватара автора курса — обогащаются
    // на бэке через IAuthorLookupClient + FileService (best-effort, null при недоступности
    // AuthService / отсутствии аватара). Нужны platform-wide списку «Курсы платформы»,
    // где admin видит курсы разных авторов и должен видеть нормального автора, а не GUID.
    string? AuthorDisplayName = null,
    string? AuthorAvatarUrl = null);
