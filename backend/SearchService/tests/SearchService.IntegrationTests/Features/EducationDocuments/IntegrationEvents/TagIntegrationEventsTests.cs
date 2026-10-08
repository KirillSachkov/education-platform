using Common;
using ContentAccess;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Core;
using SearchService.Core.Features.EducationDocuments;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;
using SearchService.IntegrationTests.Mocks;
using Shared.Messaging.IntegrationEvents.Tags.Events;
using SharedKernel;

namespace SearchService.IntegrationTests.Features.EducationDocuments.IntegrationEvents;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class TagIntegrationEventsTests : SearchServiceTestsBase
{
    public TagIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task TagsAddedToEntity_should_add_tags_to_document()
    {
        Guid materialId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await SeedTagAsync(tagId, "redis");
        await SeedEntityTagsAsync(EntityType.Material, materialId, tagId);
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Material",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        await InvokeMessageAndWaitAsync(new TagsAddedToEntity(materialId, EntityType.Material, [tagId]));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal([tagId], document.TagIds);
        Assert.Equal(["redis"], document.TagTitles);
    }

    [Fact]
    public async Task TagsAdded_before_MaterialCreated_should_not_lose_tags()
    {
        Guid materialId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await SeedTagAsync(tagId, "redis");
        await SeedEntityTagsAsync(EntityType.Material, materialId, tagId);

        await InvokeMessageAndWaitAsync(new TagsAddedToEntity(materialId, EntityType.Material, [tagId]));
        await InvokeMessageAndWaitAsync(new Shared.Messaging.IntegrationEvents.Education.Events.MaterialCreated(materialId, "PUBLIC", Guid.NewGuid()));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal(EntityType.Material, document.EntityType);
        Assert.Equal("Material title", document.Title);
        Assert.Equal([tagId], document.TagIds);
        Assert.Equal(["redis"], document.TagTitles);
    }

    [Fact]
    public async Task Pending_tag_create_after_concurrent_lifecycle_create_should_preserve_full_document()
    {
        Guid materialId = Guid.CreateVersion7();
        Guid tagId = Guid.CreateVersion7();
        DateTime updatedAt = DateTime.UtcNow;

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Lifecycle title",
            updatedAt,
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            courseId: Guid.CreateVersion7(),
            courseTitle: "Course"));

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        EducationDocumentService service =
            scope.ServiceProvider.GetRequiredService<EducationDocumentService>();

        UnitResult<Error> result = await service.CreatePendingOrUpdateExistingTagsAsync(
            EntityType.Material,
            materialId,
            [tagId],
            ["redis"]);

        Assert.True(result.IsSuccess);
        EducationDocument? document = await FindDocumentAsync(
            EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal("Lifecycle title", document.Title);
        Assert.Equal([GrantTags.AUTHENTICATED], document.RequiredAccessTags);
        Assert.Equal("Course", document.CourseTitle);
        Assert.False(document.IsDeleted);
        Assert.Equal([tagId], document.TagIds);
        Assert.Equal(["redis"], document.TagTitles);
    }

    [Fact]
    public async Task TagsAddedToUnsupportedQuiz_should_not_create_search_document()
    {
        Guid quizId = Guid.CreateVersion7();
        Guid tagId = Guid.CreateVersion7();

        await SeedTagAsync(tagId, "quiz-tag");
        await SeedEntityTagsAsync(EntityType.Quiz, quizId, tagId);

        await InvokeMessageAndWaitAsync(new TagsAddedToEntity(quizId, EntityType.Quiz, [tagId]));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateId(EntityType.Quiz, quizId));
        Assert.Null(document);
    }

    [Fact]
    public async Task TagsRemovedFromEntity_should_remove_tags_from_document()
    {
        Guid materialId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await SeedTagAsync(tagId, "redis");
        await SeedEntityTagsAsync(EntityType.Material, materialId);
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Material",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [tagId],
            tagTitles: ["redis"]));

        await InvokeMessageAndWaitAsync(new TagsRemovedFromEntity(materialId, EntityType.Material, [tagId]));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Empty(document.TagIds);
        Assert.Empty(document.TagTitles);
    }

    [Fact]
    public async Task TagUpdated_should_refresh_tag_title_in_all_documents()
    {
        Guid materialId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await SeedTagAsync(tagId, "redis");
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Material",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [tagId],
            tagTitles: ["old"]));

        await SeedTagAsync(tagId, "updated");
        await SeedEntityTagsAsync(EntityType.Material, materialId, tagId);
        await InvokeMessageAndWaitAsync(new TagUpdated(tagId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal(["updated"], document.TagTitles);
    }

    [Fact]
    public async Task TagsDeleted_should_remove_deleted_tag_from_documents()
    {
        Guid materialId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await SeedTagAsync(tagId, "redis");
        await SeedEntityTagsAsync(EntityType.Material, materialId);
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Material",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [tagId],
            tagTitles: ["redis"]));

        await RemoveTagAsync(tagId);
        await InvokeMessageAndWaitAsync(new TagsDeleted([tagId]));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Empty(document.TagIds);
        Assert.Empty(document.TagTitles);
    }

    [Fact]
    public async Task TagsDeleted_should_remove_tag_from_all_documents_in_batches()
    {
        Guid tagId = Guid.NewGuid();
        List<Guid> materialIds = [];

        await SeedTagAsync(tagId, "redis");
        await ResetTagServiceStatsAsync();

        int documentsCount = Constants.TAG_SYNC_BATCH_SIZE + 1;

        for (int i = 0; i < documentsCount; i++)
        {
            Guid materialId = Guid.NewGuid();
            materialIds.Add(materialId);
            await SeedEntityTagsAsync(EntityType.Material, materialId);

            await IndexDocumentAsync(EducationDocument.CreateMaterial(
                materialId,
                $"bulk-delete-{i}",
                DateTime.UtcNow,
                requiredAccessTags: [GrantTags.PUBLIC],
                tagIds: [tagId],
                tagTitles: ["redis"]));
        }

        await RemoveTagAsync(tagId);
        await InvokeMessageAndWaitAsync(new TagsDeleted([tagId]));

        (int lookupCalls, int maxBatchSize) = await GetTagServiceStatsAsync();
        Assert.True(lookupCalls >= 2);
        Assert.True(maxBatchSize <= Constants.TAG_SYNC_BATCH_SIZE);

        foreach (Guid materialId in materialIds)
        {
            EducationDocument? document =
                await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));

            Assert.NotNull(document);
            Assert.Empty(document.TagIds);
            Assert.Empty(document.TagTitles);
        }
    }

    private async Task ResetTagServiceStatsAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MockTagServiceClient mock = scope.ServiceProvider.GetRequiredService<MockTagServiceClient>();
        mock.ResetStats();
    }

    private async Task<(int LookupCalls, int MaxBatchSize)> GetTagServiceStatsAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MockTagServiceClient mock = scope.ServiceProvider.GetRequiredService<MockTagServiceClient>();

        return (mock.EntitiesTagsSearchLookupCalls, mock.MaxEntitiesTagsSearchLookupBatchSize);
    }

    [Fact]
    public async Task TagsMerged_should_replace_alias_with_canonical_tag()
    {
        Guid materialId = Guid.NewGuid();
        Guid aliasTagId = Guid.NewGuid();
        Guid canonicalTagId = Guid.NewGuid();

        await SeedTagAsync(aliasTagId, "alias");
        await SeedTagAsync(canonicalTagId, "canonical");
        await SeedEntityTagsAsync(EntityType.Material, materialId, canonicalTagId);
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Material",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [aliasTagId],
            tagTitles: ["alias"]));

        await InvokeMessageAndWaitAsync(new TagsMerged(canonicalTagId, [aliasTagId]));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal([canonicalTagId], document.TagIds);
        Assert.Equal(["canonical"], document.TagTitles);
    }

    [Fact]
    public async Task Material_update_should_preserve_existing_tags()
    {
        Guid materialId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await SeedTagAsync(tagId, "redis");
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Old title",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [tagId],
            tagTitles: ["redis"]));

        await InvokeMessageAndWaitAsync(new Shared.Messaging.IntegrationEvents.Education.Events.MaterialUpdated(materialId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal(EntityType.Material, document.EntityType);
        Assert.Equal("Material title", document.Title);
        Assert.Equal([tagId], document.TagIds);
        Assert.Equal(["redis"], document.TagTitles);
    }
}
