using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Common;
using ContentAccess;
using SearchService.Contracts;
using SearchService.Core;
using SearchService.Domain;
using SharedKernel;

namespace SearchService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class TypesenseProviderTests : SearchServiceTestsBase
{
    public TypesenseProviderTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task UpsertAsync_should_propagate_caller_cancellation()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ISearchProvider searchProvider = scope.ServiceProvider.GetRequiredService<ISearchProvider>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        EducationDocument document = EducationDocument.CreateMaterial(
            Guid.CreateVersion7(),
            "cancelled",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => searchProvider.UpsertAsync(
            CollectionNames.EDUCATION_SEARCH,
            document,
            cancellation.Token));
    }

    [Fact]
    public async Task EmplaceAsync_should_preserve_existing_tags_and_create_missing_document()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ISearchProvider searchProvider = scope.ServiceProvider.GetRequiredService<ISearchProvider>();
        Guid existingId = Guid.CreateVersion7();
        Guid missingId = Guid.CreateVersion7();
        Guid tagId = Guid.CreateVersion7();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            existingId,
            "old",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [tagId],
            tagTitles: ["preserved"]));

        UnitResult<Error> existingResult = await searchProvider.EmplaceAsync<EducationDocument>(
            CollectionNames.EDUCATION_SEARCH,
            EducationDocument.CreateMaterialId(existingId),
            update => update
                .Set(x => x.EntityId, existingId)
                .Set(x => x.EntityType, EntityType.Material)
                .Set(x => x.Title, "updated")
                .Set(x => x.RequiredAccessTags, [GrantTags.PUBLIC])
                .Set(x => x.UpdatedAtTicks, DateTime.UtcNow.Ticks)
                .Set(x => x.IsDeleted, false)
                .Set(x => x.ReindexGeneration, EducationDocument.LIVE_REINDEX_GENERATION));

        UnitResult<Error> missingResult = await searchProvider.EmplaceAsync<EducationDocument>(
            CollectionNames.EDUCATION_SEARCH,
            EducationDocument.CreateMaterialId(missingId),
            update => update
                .Set(x => x.EntityId, missingId)
                .Set(x => x.EntityType, EntityType.Material)
                .Set(x => x.Title, "created")
                .Set(x => x.RequiredAccessTags, [GrantTags.PUBLIC])
                .Set(x => x.UpdatedAtTicks, DateTime.UtcNow.Ticks)
                .Set(x => x.IsDeleted, false)
                .Set(x => x.ReindexGeneration, EducationDocument.LIVE_REINDEX_GENERATION));

        Assert.True(existingResult.IsSuccess);
        Assert.True(missingResult.IsSuccess);

        EducationDocument? existing = await FindDocumentAsync(EducationDocument.CreateMaterialId(existingId));
        EducationDocument? created = await FindDocumentAsync(EducationDocument.CreateMaterialId(missingId));
        Assert.NotNull(existing);
        Assert.Equal("updated", existing.Title);
        Assert.Equal([tagId], existing.TagIds);
        Assert.Equal(["preserved"], existing.TagTitles);
        Assert.NotNull(created);
        Assert.Equal("created", created.Title);
        Assert.Empty(created.TagIds);
        Assert.Empty(created.TagTitles);
    }

    [Fact]
    public async Task UpdateManyAsync_should_fail_when_typesense_returns_document_import_error()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ISearchProvider searchProvider = scope.ServiceProvider.GetRequiredService<ISearchProvider>();

        UnitResult<Error> result = await searchProvider.UpdateManyAsync<EducationDocument>(
            CollectionNames.EDUCATION_SEARCH,
            [
                new SearchDocumentUpdate<EducationDocument>(
                    EducationDocument.CreateMaterialId(Guid.NewGuid()),
                    update => update.Set(static document => document.Title, "Updated title"))
            ],
            batchSize: 100,
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SearchAsync_should_page_filtered_documents_without_materializing_full_export()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ISearchProvider searchProvider = scope.ServiceProvider.GetRequiredService<ISearchProvider>();
        Guid tagId = Guid.NewGuid();

        for (int i = 0; i < Constants.TAG_SYNC_FETCH_PAGE_SIZE + 1; i++)
        {
            EducationDocument document = EducationDocument.CreateMaterial(
                Guid.NewGuid(),
                $"material-{i}",
                DateTime.UtcNow,
                requiredAccessTags: [GrantTags.PUBLIC],
                tagIds: [tagId],
                tagTitles: ["redis"]);

            UnitResult<Error> upsertResult = await searchProvider.UpsertAsync(
                CollectionNames.EDUCATION_SEARCH,
                document,
                CancellationToken.None);

            Assert.True(upsertResult.IsSuccess);
        }

        // limitHits must exceed the total document count (251) so Typesense reports
        // the correct `found` value and doesn't silently cap TotalCount at `per_page`.
        int limitHits = Constants.TAG_SYNC_FETCH_PAGE_SIZE * Constants.TAG_SYNC_MAX_PAGES;

        Result<SearchResponse<EducationDocument>, Error> firstPageResult =
            await searchProvider.SearchAsync<EducationDocument>(
                CollectionNames.EDUCATION_SEARCH,
                new SearchRequest("*", 1, Constants.TAG_SYNC_FETCH_PAGE_SIZE),
                "title",
                $"tag_ids:=[`{tagId:D}`]",
                includeFields: Constants.TAG_SYNC_INCLUDE_FIELDS,
                limitHits: limitHits,
                cancellationToken: CancellationToken.None);

        Result<SearchResponse<EducationDocument>, Error> secondPageResult =
            await searchProvider.SearchAsync<EducationDocument>(
                CollectionNames.EDUCATION_SEARCH,
                new SearchRequest("*", 2, Constants.TAG_SYNC_FETCH_PAGE_SIZE),
                "title",
                $"tag_ids:=[`{tagId:D}`]",
                includeFields: Constants.TAG_SYNC_INCLUDE_FIELDS,
                limitHits: limitHits,
                cancellationToken: CancellationToken.None);

        Assert.True(firstPageResult.IsSuccess);
        Assert.True(secondPageResult.IsSuccess);
        Assert.Equal(Constants.TAG_SYNC_FETCH_PAGE_SIZE + 1, firstPageResult.Value.TotalCount);
        Assert.Equal(Constants.TAG_SYNC_FETCH_PAGE_SIZE, firstPageResult.Value.Hits.Count);
        Assert.Single(secondPageResult.Value.Hits);
        Assert.All(firstPageResult.Value.Hits, hit =>
        {
            Assert.NotEqual(string.Empty, hit.Document.Id);
            Assert.Equal(EntityType.Material, hit.Document.EntityType);
            Assert.NotEqual(Guid.Empty, hit.Document.EntityId);
        });
    }
}
