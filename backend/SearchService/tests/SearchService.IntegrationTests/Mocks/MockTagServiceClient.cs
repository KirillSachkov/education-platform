using System.Collections.Concurrent;
using Common;
using CSharpFunctionalExtensions;
using SharedKernel;
using TagService.Contracts.HttpCommunication;
using TagService.Contracts.SearchLookup;

namespace SearchService.IntegrationTests.Mocks;

public sealed class MockTagServiceClient : ITagServiceClient
{
    private readonly ConcurrentDictionary<Guid, string> _titlesById = new();
    private readonly ConcurrentDictionary<EntityType, ConcurrentDictionary<Guid, Guid[]>> _entityTagsByType = new();
    private int _entitiesTagsSearchLookupCalls;
    private int _maxEntitiesTagsSearchLookupBatchSize;

    public int EntitiesTagsSearchLookupCalls => _entitiesTagsSearchLookupCalls;

    public int MaxEntitiesTagsSearchLookupBatchSize => _maxEntitiesTagsSearchLookupBatchSize;

    public Task<Result<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error>> GetEntitiesTagsSearchLookupAsync(
        IReadOnlyList<EntityTagsSearchLookupBatchItem> entities,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _entitiesTagsSearchLookupCalls);
        UpdateMaxBatchSize(entities.Count);

        IReadOnlyList<EntityTagsSearchLookupBatchDto> tags = entities
            .Distinct()
            .Where(entity => _entityTagsByType.TryGetValue(entity.EntityType, out var entityTags)
                && entityTags.ContainsKey(entity.EntityId))
            .Select(entity =>
            {
                Guid[] tagIds = _entityTagsByType[entity.EntityType][entity.EntityId]
                    .Where(_titlesById.ContainsKey)
                    .ToArray();
                string[] tagTitles = tagIds
                    .Select(tagId => _titlesById[tagId])
                    .ToArray();

                return new EntityTagsSearchLookupBatchDto(
                    entity.EntityType,
                    entity.EntityId,
                    tagIds,
                    tagTitles);
            })
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error>(tags));
    }

    public void Seed(Guid tagId, string title) => _titlesById[tagId] = title;

    public void Remove(Guid tagId) => _titlesById.TryRemove(tagId, out _);

    public void ResetStats()
    {
        Interlocked.Exchange(ref _entitiesTagsSearchLookupCalls, 0);
        Interlocked.Exchange(ref _maxEntitiesTagsSearchLookupBatchSize, 0);
    }

    public void SeedEntityTags(EntityType entityType, Guid entityId, params Guid[] tagIds)
    {
        ConcurrentDictionary<Guid, Guid[]> entityTags =
            _entityTagsByType.GetOrAdd(entityType, _ => new ConcurrentDictionary<Guid, Guid[]>());

        entityTags[entityId] = tagIds
            .Distinct()
            .ToArray();
    }

    private void UpdateMaxBatchSize(int batchSize)
    {
        int current;
        do
        {
            current = _maxEntitiesTagsSearchLookupBatchSize;
            if (batchSize <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _maxEntitiesTagsSearchLookupBatchSize, batchSize, current) != current);
    }
}
