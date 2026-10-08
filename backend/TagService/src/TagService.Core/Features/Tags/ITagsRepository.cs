using System.Linq.Expressions;
using TagService.Domain.EntityTags;
using TagService.Domain.TagAliases;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags;

public interface ITagsRepository
{
    Task<UnitResult<Error>> AcquireMutationLocksAsync(
        IReadOnlyCollection<TagId> tagIds,
        CancellationToken cancellationToken = default);

    Task<Result<Tag, Error>> GetBy(Expression<Func<Tag, bool>> predicate, CancellationToken cancellationToken = default);

    Task<bool> CheckExistsTag(TagId tagId, CancellationToken cancellationToken = default);

    Task<bool> CheckAllTagsExistAsync(IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetTagAuthorIdsAsync(
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyAlias(IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TagId>> GetCanonTagIdsByIdsAsync(
        IReadOnlyList<TagId> tagIds,
        Guid? authorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CanonTagByTitle>> GetCanonTagIdsByTitlesAsync(
        IReadOnlyList<string> titles,
        Guid? authorId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Tag tag, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyList<Tag> tags, CancellationToken cancellationToken = default);

    Task AddAliasesAsync(IReadOnlyList<TagAlias> aliases, CancellationToken cancellationToken = default);

    Task AddTagsToEntityAsync(IReadOnlyList<EntityTag> entityTags, CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> DeleteTagAsync(TagId tagId, CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> DeleteCanonicalTagAndRestoreAliasesAsync(
        TagId tagId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<TagId>, Error>> RemoveTagsFromEntityAsync(
        TagEntityReference entityReference,
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> MergeEntityTagsAsync(
        TagId canonicalTagId,
        IReadOnlyList<TagId> aliasTagIds,
        CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> MarkTagsCanonAsync(IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> MarkOrphanAliasesCanonAsync(
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> MarkTagsAliasAsync(IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<TagId>, Error>> RemoveAliasesAsync(
        TagId tagId,
        IReadOnlyCollection<TagId> aliasTagIds,
        CancellationToken cancellationToken = default);
}
