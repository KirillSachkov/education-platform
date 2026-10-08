using System.Collections.Frozen;
using Common;

namespace SearchService.Core;

public static class Constants
{
    public const int MIN_PAGE_SIZE = 1;
    public const int MAX_PAGE_SIZE = 100;
    public const int MAX_RESULT_WINDOW = 10_000;
    public const int MAX_QUERY_LENGTH = 256;
    public const int MAX_FILTER_VALUES = 50;
    public const int TAG_SYNC_BATCH_SIZE = 500;
    public const int TAG_SYNC_FETCH_PAGE_SIZE = 250;
    public const int TAG_SYNC_MAX_PAGES = 1000;
    public const string TAG_SYNC_INCLUDE_FIELDS = "id,entity_type,entity_id";

    /// <summary>
    /// Допустимые значения фильтра <c>material_kind</c>. Зеркалит
    /// <c>EducationContentService.Domain.Materials.MaterialKind</c> (ARTICLE/VIDEO/NOTE/STREAM)
    /// — held as строки, потому что SearchService хранит kind строкой во всём пайплайне.
    /// Используется для allowlist-валидации перед подстановкой в Typesense <c>filter_by</c>:
    /// <c>material_kind</c> — единственный free-string параметр фильтра, поэтому без проверки
    /// крафтнутое значение (backtick + <c>||</c>) могло бы изменить семантику фильтра и
    /// раскрыть soft-deleted/draft-документы (filter injection). Ordinal + case-sensitive —
    /// stored-значения в UPPER_SNAKE, а Typesense equality регистрозависим.
    /// </summary>
    public static readonly FrozenSet<string> ALLOWED_MATERIAL_KINDS =
        new[] { "ARTICLE", "VIDEO", "NOTE", "STREAM" }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly FrozenSet<EntityType> SEARCHABLE_ENTITY_TYPES =
        new[]
        {
            EntityType.Course,
            EntityType.Module,
            EntityType.Project,
            EntityType.Issue,
            EntityType.Material,
            EntityType.Collection,
        }.ToFrozenSet();
}
