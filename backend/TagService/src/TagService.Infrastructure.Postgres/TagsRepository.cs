using System.Data.Common;
using System.Linq.Expressions;
using Core.Database;
using Dapper;
using Microsoft.Extensions.Logging;
using TagService.Core.Features.Tags;
using TagService.Domain.EntityTags;
using TagService.Domain.TagAliases;
using TagService.Domain.Tags;

namespace TagService.Infrastructure.Postgres;

public sealed class TagsRepository : ITagsRepository
{
    private readonly TagDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<TagsRepository> _logger;

    public TagsRepository(
        TagDbContext dbContext,
        ITransactionManager transactionManager,
        ILogger<TagsRepository> logger)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task<Result<Tag, Error>> GetBy(Expression<Func<Tag, bool>> predicate, CancellationToken cancellationToken = default)
    {
        Tag? tag = await _dbContext.Tags.FirstOrDefaultAsync(predicate, cancellationToken);

        if (tag is null)
            return Error.NotFound("tag.database.notfound", "Тег не найден");

        return tag;
    }

    public Task<bool> CheckExistsTag(TagId tagId, CancellationToken cancellationToken = default) =>
        _dbContext.Tags.AnyAsync(x => x.Id == tagId, cancellationToken);

    public async Task<bool> CheckAllTagsExistAsync(IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT COUNT(*)
                           FROM tags
                           WHERE id = ANY(@Ids::uuid[]);
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        int count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { Ids = tagIds.Select(x => x.Value).ToArray() },
            cancellationToken: cancellationToken));

        return count == tagIds.Count;
    }

    public async Task<IReadOnlyList<Guid>> GetTagAuthorIdsAsync(
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT DISTINCT author_id
                           FROM tags
                           WHERE id = ANY(@Ids::uuid[]);
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<Guid> authorIds = await connection.QueryAsync<Guid>(new CommandDefinition(
            sql,
            new { Ids = tagIds.Select(x => x.Value).ToArray() },
            cancellationToken: cancellationToken));

        return authorIds.ToList();
    }

    public Task<bool> HasAnyAlias(IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default) =>
        _dbContext.Tags
            .AnyAsync(x => tagIds.Contains(x.Id) && x.Kind == TagKind.ALIAS, cancellationToken);

    public async Task<IReadOnlyList<TagId>> GetCanonTagIdsByIdsAsync(
        IReadOnlyList<TagId> tagIds,
        Guid? authorId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT COALESCE(ta.tag_id, t.id)
                           FROM UNNEST(@TagIds::uuid[]) AS x(tag_id)
                           JOIN tags t
                               ON t.id = x.tag_id
                           LEFT JOIN tag_aliases ta
                               ON ta.alias_tag_id = x.tag_id
                           LEFT JOIN tags canonical
                               ON canonical.id = ta.tag_id
                           WHERE @AuthorId IS NULL
                              OR (t.author_id = @AuthorId
                                  AND (canonical.id IS NULL OR canonical.author_id = @AuthorId));
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(new CommandDefinition(
            sql,
            new { TagIds = tagIds.Select(x => x.Value).ToArray(), AuthorId = authorId },
            cancellationToken: cancellationToken));

        return TagId.Of(ids).ToArray();
    }

    public async Task<IReadOnlyList<CanonTagByTitle>> GetCanonTagIdsByTitlesAsync(
        IReadOnlyList<string> titles,
        Guid? authorId,
        CancellationToken cancellationToken = default)
    {
        // Case-insensitive title match — callers pass titles with arbitrary casing
        // ("React" vs "react"). Slug uniqueness already enforces a single normalized
        // form, so titles differing only in case map to the same canon tag.
        const string sql = """
                           SELECT DISTINCT
                               COALESCE(ta.tag_id, t.id) AS id,
                               x.title
                           FROM UNNEST(@Titles::text[]) AS x(title)
                           JOIN tags t
                               ON LOWER(t.title) = LOWER(x.title)
                           LEFT JOIN tag_aliases ta
                               ON ta.alias_tag_id = t.id
                           LEFT JOIN tags canonical
                               ON canonical.id = ta.tag_id
                           WHERE @AuthorId IS NULL
                              OR (t.author_id = @AuthorId
                                  AND (canonical.id IS NULL OR canonical.author_id = @AuthorId));
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<CanonTagByTitle> rows = await connection.QueryAsync<CanonTagByTitle>(new CommandDefinition(
            sql,
            new { Titles = titles.ToArray(), AuthorId = authorId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task AddAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        await _dbContext.Tags.AddAsync(tag, cancellationToken);
    }

    public async Task AddRangeAsync(IReadOnlyList<Tag> tags, CancellationToken cancellationToken = default)
    {
        await _dbContext.Tags.AddRangeAsync(tags, cancellationToken);
    }

    public async Task AddAliasesAsync(IReadOnlyList<TagAlias> aliases, CancellationToken cancellationToken = default)
    {
        await _dbContext.TagAliases.AddRangeAsync(aliases, cancellationToken);
    }

    public async Task AddTagsToEntityAsync(IReadOnlyList<EntityTag> entityTags, CancellationToken cancellationToken = default)
    {
        await _dbContext.EntityTags.AddRangeAsync(entityTags, cancellationToken);
    }

    public async Task<UnitResult<Error>> DeleteTagAsync(TagId tagId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.Tags
                .Where(x => x.Id == tagId)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tag {TagId}", tagId.Value);
            return UnitResult.Failure(GeneralErrors.InvalidOperation("Ошибка при удалении тега"));
        }

        return UnitResult.Success<Error>();
    }

    public async Task<Result<IReadOnlyList<TagId>, Error>> RemoveTagsFromEntityAsync(
        TagEntityReference entityReference,
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                DELETE FROM entity_tags
                WHERE entity_type = @EntityType
                  AND entity_id = @EntityId
                  AND tag_id = ANY(@TagIds::uuid[])
                RETURNING tag_id;
                """;

            DbConnection connection = _transactionManager.GetDbConnection();
            IEnumerable<Guid> removedTagIds = await connection.QueryAsync<Guid>(new CommandDefinition(
                sql,
                new
                {
                    EntityType = entityReference.Type.ToString().ToLowerInvariant(),
                    EntityId = entityReference.Id,
                    TagIds = tagIds.Select(x => x.Value).ToArray()
                },
                cancellationToken: cancellationToken));

            return TagId.Of(removedTagIds).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove tags from entity {EntityType}:{EntityId}", entityReference.Type, entityReference.Id);
            return GeneralErrors.InvalidOperation("Ошибка при удалении тегов у сущности");
        }
    }

    public async Task<UnitResult<Error>> MergeEntityTagsAsync(
        TagId canonicalTagId,
        IReadOnlyList<TagId> aliasTagIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                WITH ranked AS (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY entity_type, entity_id
                               ORDER BY CASE WHEN tag_id = @CanonicalTagId THEN 0 ELSE 1 END, id
                           ) AS row_number
                    FROM entity_tags
                    WHERE tag_id = @CanonicalTagId
                       OR tag_id = ANY(@AliasTagIds::uuid[])
                )
                DELETE FROM entity_tags et
                USING ranked r
                WHERE et.id = r.id
                  AND r.row_number > 1;

                UPDATE entity_tags
                SET tag_id = @CanonicalTagId
                WHERE tag_id = ANY(@AliasTagIds::uuid[]);
                """;

            DbConnection connection = _transactionManager.GetDbConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    CanonicalTagId = canonicalTagId.Value,
                    AliasTagIds = aliasTagIds.Select(x => x.Value).ToArray()
                },
                cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to merge entity tags. CanonicalTagId={TagId}", canonicalTagId.Value);
            return UnitResult.Failure(GeneralErrors.InvalidOperation("Ошибка при замене тегов в сущностях"));
        }

        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> MarkTagsCanonAsync(IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.Tags
                .Where(x => tagIds.Contains(x.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Kind, _ => TagKind.CANON),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark tags as canon. Count={Count}", tagIds.Count);
            return UnitResult.Failure(GeneralErrors.InvalidOperation("Ошибка при обновлении статуса тегов"));
        }

        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> AcquireMutationLocksAsync(
        IReadOnlyCollection<TagId> tagIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                SELECT pg_advisory_xact_lock(lock_key)
                FROM (
                    SELECT DISTINCT hashtextextended(tag_id::text, 0) AS lock_key
                    FROM unnest(@TagIds::uuid[]) AS requested(tag_id)
                ) requested_locks
                ORDER BY lock_key;
                """;

            DbConnection connection = _transactionManager.GetDbConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { TagIds = tagIds.Select(x => x.Value).ToArray() },
                cancellationToken: cancellationToken));
            return UnitResult.Success<Error>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire tag mutation locks. Count={Count}", tagIds.Count);
            return UnitResult.Failure(GeneralErrors.DatabaseError());
        }
    }

    public async Task<UnitResult<Error>> DeleteCanonicalTagAndRestoreAliasesAsync(
        TagId tagId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                WITH aliases AS MATERIALIZED (
                    SELECT alias_tag_id
                    FROM tag_aliases
                    WHERE tag_id = @TagId
                ), deleted AS (
                    DELETE FROM tags
                    WHERE id = @TagId
                    RETURNING id
                )
                UPDATE tags
                SET kind = @CanonKind,
                    updated_at = timezone('utc', now())
                WHERE id IN (SELECT alias_tag_id FROM aliases)
                  AND EXISTS (SELECT 1 FROM deleted);
                """;

            DbConnection connection = _transactionManager.GetDbConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { TagId = tagId.Value, CanonKind = TagKind.CANON.ToString() },
                cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete canonical tag {TagId} and restore aliases", tagId.Value);
            return UnitResult.Failure(GeneralErrors.InvalidOperation("Ошибка при удалении канонического тега"));
        }

        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> MarkOrphanAliasesCanonAsync(
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.Tags
                .Where(tag => tagIds.Contains(tag.Id)
                    && !_dbContext.TagAliases.Any(alias => alias.AliasTagId == tag.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Kind, _ => TagKind.CANON),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore orphan aliases. Count={Count}", tagIds.Count);
            return UnitResult.Failure(GeneralErrors.InvalidOperation("Ошибка при восстановлении статуса тегов"));
        }

        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> MarkTagsAliasAsync(IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.Tags
                .Where(x => tagIds.Contains(x.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Kind, _ => TagKind.ALIAS),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark tags as alias. Count={Count}", tagIds.Count);
            return UnitResult.Failure(GeneralErrors.InvalidOperation("Ошибка при обновлении статуса тегов"));
        }

        return UnitResult.Success<Error>();
    }

    public async Task<Result<IReadOnlyList<TagId>, Error>> RemoveAliasesAsync(
        TagId tagId,
        IReadOnlyCollection<TagId> aliasTagIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                DELETE FROM tag_aliases
                WHERE tag_id = @TagId
                  AND alias_tag_id = ANY(@AliasTagIds::uuid[])
                RETURNING alias_tag_id;
                """;

            DbConnection connection = _transactionManager.GetDbConnection();
            IEnumerable<Guid> removedAliasIds = await connection.QueryAsync<Guid>(new CommandDefinition(
                sql,
                new
                {
                    TagId = tagId.Value,
                    AliasTagIds = aliasTagIds.Select(x => x.Value).ToArray()
                },
                cancellationToken: cancellationToken));

            return TagId.Of(removedAliasIds).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove aliases for tag {TagId}. Count={Count}", tagId.Value, aliasTagIds.Count);
            return GeneralErrors.InvalidOperation("Ошибка при удалении алиасов тега");
        }
    }
}
