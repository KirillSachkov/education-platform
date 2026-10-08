using System.Data.Common;
using Dapper;

namespace EducationContentService.Core.Features.Collections.Queries;

/// <summary>
///     Грузит per-collection список <c>(itemType, referenceId, accessType, courseIds)</c> для
///     items внутри подборок (generic после #491: материалы + квизы). Используется и в
///     list-эндпоинтах (<see cref="CollectionAccessEnricher"/> решает «есть ли видимые items»
///     для карточки), и в detail-эндпоинте (per-item lock-icon).
///
///     <para>
///     MATERIAL-строки: access_type/author из <c>materials</c>, привязки — <c>course_materials</c>.
///     QUIZ-строки: access_type/author из <c>quizzes</c>, привязки — <c>course_quizzes</c>.
///     Учитываются только PUBLISHED-сущности — DRAFT не виден не-авторам, так что
///     на видимость подборки они не влияют.
///     </para>
/// </summary>
internal static class CollectionItemAccessLoader
{
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CollectionItemAccessRow>>> LoadAsync(
        DbConnection connection,
        IReadOnlyCollection<Guid> collectionIds,
        CancellationToken cancellationToken)
    {
        if (collectionIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<CollectionItemAccessRow>>();

        const string sql = """
                           SELECT
                               cs.collection_id      AS collection_id,
                               ci.item_type          AS item_type,
                               m.id                  AS reference_id,
                               m.access_type         AS access_type,
                               m.author_id           AS author_id,
                               COALESCE(
                                   (SELECT array_agg(cm.course_id ORDER BY cm.course_id)
                                    FROM course_materials cm
                                    WHERE cm.material_id = m.id),
                                   ARRAY[]::uuid[]
                               )                     AS course_ids
                           FROM collection_sections cs
                           JOIN collection_items ci ON ci.section_id = cs.id AND ci.item_type = 'MATERIAL'
                           JOIN materials m         ON m.id = ci.reference_id AND m.status = 'PUBLISHED'
                           WHERE cs.collection_id = ANY(@CollectionIds)

                           UNION ALL

                           SELECT
                               cs.collection_id      AS collection_id,
                               ci.item_type          AS item_type,
                               q.id                  AS reference_id,
                               q.access_type         AS access_type,
                               q.author_id           AS author_id,
                               COALESCE(
                                   (SELECT array_agg(cq.course_id ORDER BY cq.course_id)
                                    FROM course_quizzes cq
                                    WHERE cq.quiz_id = q.id),
                                   ARRAY[]::uuid[]
                               )                     AS course_ids
                           FROM collection_sections cs
                           JOIN collection_items ci ON ci.section_id = cs.id AND ci.item_type = 'QUIZ'
                           JOIN quizzes q           ON q.id = ci.reference_id AND q.status = 'PUBLISHED'
                           WHERE cs.collection_id = ANY(@CollectionIds);
                           """;

        IEnumerable<ItemRow> rows = await connection.QueryAsync<ItemRow>(
            new CommandDefinition(
                sql,
                new { CollectionIds = collectionIds.ToArray() },
                cancellationToken: cancellationToken));

        Dictionary<Guid, List<CollectionItemAccessRow>> grouped = new();
        foreach (Guid id in collectionIds)
            grouped[id] = [];

        foreach (ItemRow r in rows)
        {
            grouped[r.CollectionId].Add(new CollectionItemAccessRow(
                r.ItemType,
                r.ReferenceId,
                r.AccessType,
                r.CourseIds ?? [],
                r.AuthorId));
        }

        return grouped.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<CollectionItemAccessRow>)kv.Value);
    }

    private sealed class ItemRow
    {
        public Guid CollectionId { get; init; }
        public string ItemType { get; init; } = null!;
        public Guid ReferenceId { get; init; }
        public string AccessType { get; init; } = null!;
        public Guid AuthorId { get; init; }
        public Guid[]? CourseIds { get; init; }
    }
}
