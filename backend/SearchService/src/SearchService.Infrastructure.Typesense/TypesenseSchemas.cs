using Typesense;

namespace SearchService.Infrastructure.Typesense;

public static class TypesenseSchemas
{
    // Typesense поддерживает русскую морфологию через locale="ru" — включаем стеммер и
    // нормализацию регистра. Без этого «постгрес» и «постгресу» индексируются как разные
    // токены, из-за чего типовые запросы пользователей по КБ/Ctrl+K работают хуже.
    private const string RU_LOCALE = "ru";

    public static Schema CreateEducationSearchSchema(string collectionName) => new(collectionName, new List<Field>
    {
        new("id", FieldType.String, facet: false, optional: false),
        new("entity_type", FieldType.String, facet: true, optional: false),
        new("entity_id", FieldType.String, facet: true, optional: false),
        new("title", FieldType.String, facet: false, optional: false) { Locale = RU_LOCALE },
        new("description", FieldType.String, facet: false, optional: true) { Locale = RU_LOCALE },
        // content — полный markdown-текст материала (ARTICLE/NOTE). Храним до 200 КБ
        // (SUBSTRING в экспорте). В response не отдаётся (exclude_fields) — только сниппет
        // через highlight. Только Material имеет эту колонку; у остальных entity_type null.
        new("content", FieldType.String, facet: false, optional: true, index: true) { Locale = RU_LOCALE },
        // chapter_titles — заголовки глав видео (Kinescope chapters), денормализованные
        // из Material.ChapterTitles. Используется в SEARCH_FIELDS / HIGHLIGHT_FIELDS
        // (см. GetDocumentsHandler) — без поля в schema Typesense возвращает 404 на
        // query_by с unknown field. Для array-полей Typesense возвращает highlight.indices
        // (массив индексов совпавших элементов), который провайдер кладёт в
        // SearchHighlight.MatchedIndices.
        new("chapter_titles", FieldType.StringArray, facet: false, optional: true, index: true) { Locale = RU_LOCALE },
        // chapter_timestamps — параллельный массив offset'ов глав в секундах.
        // Не индексируем (поиск по числам не нужен); хранится как payload, фронт по
        // matched chapter index из highlight'а резолвит chapter_timestamps[i] →
        // ?t=<seconds> deep-link в Kinescope.
        new("chapter_timestamps", FieldType.Int32Array, facet: false, optional: true, index: false),
        new("image_id", FieldType.String, facet: false, optional: true, index: false),
        new("required_access_tags", FieldType.StringArray, facet: true, optional: false, index: true),
        new("tag_ids", FieldType.StringArray, facet: true, optional: true, index: true),
        new("tag_titles", FieldType.StringArray, facet: false, optional: true, index: true) { Locale = RU_LOCALE },
        // index: true + sort: true — требование Typesense для использования поля в sort_by.
        // Без index=true (было раньше) sort_by=updated_at_ticks:desc отваливался с
        // "Could not find a field named `updated_at_ticks` in the schema for sorting".
        new("updated_at_ticks", FieldType.Int64, facet: false, optional: false, index: true, sort: true, infix: false),
        new("is_deleted", FieldType.Bool, facet: true, optional: false),
        new("reindex_generation", FieldType.String, facet: true, optional: false, index: true),
        new("course_id", FieldType.String, facet: true, optional: true, index: true),
        new("course_slug", FieldType.String, facet: false, optional: true, index: true),
        new("course_title", FieldType.String, facet: false, optional: true) { Locale = RU_LOCALE },
        new("course_access_type", FieldType.String, facet: true, optional: true),
        new("author_id", FieldType.String, facet: true, optional: true, index: true),
        new("project_id", FieldType.String, facet: true, optional: true, index: true),
        new("project_title", FieldType.String, facet: false, optional: true) { Locale = RU_LOCALE },
        new("module_id", FieldType.String, facet: true, optional: true, index: true),
        new("module_title", FieldType.String, facet: false, optional: true) { Locale = RU_LOCALE },
        new("material_kind", FieldType.String, facet: true, optional: true, index: true),
        // video_id — payload-поле: не индексируем и не фасетим, только лежит в документе,
        // чтобы handler поиска мог резолвнуть thumbnail через FileService batch.
        new("video_id", FieldType.String, facet: false, optional: true, index: false),
    });
}
