using Common;
using ContentAccess;
using EducationContentService.Contracts.SearchExport;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SearchService.Core.Reindex;
using SearchService.Core.Reindex.State;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;
using SearchService.IntegrationTests.Mocks;
using Typesense;

namespace SearchService.IntegrationTests.Features.Reindex;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ReindexTests : SearchServiceTestsBase
{
    public ReindexTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Full_reindex_should_truncate_index_and_import_exported_documents_with_tags()
    {
        Guid staleCourseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid collectionId = Guid.CreateVersion7();
        Guid courseId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();
        string oldAliasTarget = await GetEducationSearchAliasTargetAsync();

        await IndexDocumentAsync(EducationDocument.CreateCourse(
            staleCourseId,
            "Stale course",
            "stale",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        await SeedTagAsync(tagId, "redis");
        await SeedEntityTagsAsync(EntityType.Material, materialId, tagId);
        await ConfigureExportDocumentsAsync(
            EntityType.Material,
            new SearchExportEntityDto
            {
                EntityType = EntityType.Material,
                EntityId = materialId,
                Title = "Material title",
                CourseId = courseId,
                CourseTitle = "Course title",
                ModuleId = Guid.NewGuid(),
                ModuleTitle = "Module title",
                RequiredAccessTags = [GrantTags.CourseTrial(courseId)],
                UpdatedAt = new DateTime(2024, 1, 20, 10, 0, 0, DateTimeKind.Utc),
            });
        await ConfigureExportDocumentsAsync(
            EntityType.Collection,
            new SearchExportEntityDto
            {
                EntityType = EntityType.Collection,
                EntityId = collectionId,
                Title = "Collection title",
                Description = "Collection description",
                CourseId = courseId,
                CourseTitle = "Course title",
                RequiredAccessTags = [GrantTags.PUBLIC],
                UpdatedAt = new DateTime(2024, 1, 20, 11, 0, 0, DateTimeKind.Utc),
            });

        await using (AsyncServiceScope setupScope = Services.CreateAsyncScope())
        {
            setupScope.ServiceProvider
                .GetRequiredService<IEducationContentServiceClient>()
                .ClearReceivedCalls();
        }

        await RunFullReindexAsync();

        await using (AsyncServiceScope assertionScope = Services.CreateAsyncScope())
        {
            IEducationContentServiceClient educationClient =
                assertionScope.ServiceProvider.GetRequiredService<IEducationContentServiceClient>();

            await educationClient.ReceivedWithAnyArgs().ExportAllSearchEntitiesAsync(default, default, default);
        }

        EducationDocument? staleCourse =
            await FindDocumentAsync(EducationDocument.CreateCourseId(staleCourseId));
        EducationDocument? material =
            await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        EducationDocument? collection =
            await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));

        Assert.Null(staleCourse);
        Assert.NotNull(material);
        Assert.NotNull(collection);
        Assert.Equal("Collection title", collection.Title);
        Assert.Equal(["redis"], material.TagTitles);
        Assert.Equal([tagId], material.TagIds);

        string newAliasTarget = await GetEducationSearchAliasTargetAsync();
        Assert.NotEqual(oldAliasTarget, newAliasTarget);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITypesenseClient typesenseClient = scope.ServiceProvider.GetRequiredService<ITypesenseClient>();
        await Assert.ThrowsAsync<TypesenseApiNotFoundException>(
            () => typesenseClient.RetrieveCollection(oldAliasTarget));
    }

    [Fact]
    public async Task Partial_reindex_should_replace_only_selected_entity_type()
    {
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid staleMaterialId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateCourse(
            courseId,
            "Course title",
            "Course description",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Old material",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            staleMaterialId,
            "Stale material",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        await SeedTagAsync(tagId, "postgres");
        await SeedEntityTagsAsync(EntityType.Material, materialId, tagId);
        await ConfigureExportDocumentsAsync(
            EntityType.Material,
            new SearchExportEntityDto
            {
                EntityType = EntityType.Material,
                EntityId = materialId,
                Title = "New material",
                CourseId = courseId,
                CourseTitle = "Course title",
                ModuleId = Guid.NewGuid(),
                ModuleTitle = "Module title",
                RequiredAccessTags = [GrantTags.Course(courseId)],
                UpdatedAt = new DateTime(2024, 1, 21, 10, 0, 0, DateTimeKind.Utc),
            });

        await using AsyncServiceScope setupScope = Services.CreateAsyncScope();
        var consumerController = (NoopSearchIndexingConsumerController)setupScope.ServiceProvider
            .GetRequiredService<ISearchIndexingConsumerController>();
        int pauseCallsBefore = consumerController.PauseCalls;

        await RunPartialReindexAsync(EntityType.Material);

        EducationDocument? course =
            await FindDocumentAsync(EducationDocument.CreateCourseId(courseId));
        EducationDocument? material =
            await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        EducationDocument? staleMaterial =
            await FindDocumentAsync(EducationDocument.CreateMaterialId(staleMaterialId));

        Assert.NotNull(course);
        Assert.NotNull(material);
        Assert.Null(staleMaterial);
        Assert.Equal(pauseCallsBefore + 1, consumerController.PauseCalls);
        Assert.Equal("New material", material.Title);
        Assert.Equal(["postgres"], material.TagTitles);
    }

    [Fact]
    public async Task Full_reindex_should_not_mark_generation_applied_when_consumers_fail_to_resume()
    {
        await ConfigureExportDocumentsAsync<SearchExportEntityDto>(EntityType.Material);

        await using (AsyncServiceScope setupScope = Services.CreateAsyncScope())
        {
            var controller = (NoopSearchIndexingConsumerController)setupScope.ServiceProvider
                .GetRequiredService<ISearchIndexingConsumerController>();
            controller.FailNextResume();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunFullReindexAsync());

        await using AsyncServiceScope assertionScope = Services.CreateAsyncScope();
        ISearchReindexStateRepository repository = assertionScope.ServiceProvider
            .GetRequiredService<ISearchReindexStateRepository>();
        SearchReindexState state = await repository.GetOrInitAsync();
        Assert.Null(state.LastAppliedAtUtc);
        Assert.Null(state.LastRequestId);
    }

    private async Task<string> GetEducationSearchAliasTargetAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITypesenseClient typesenseClient = scope.ServiceProvider.GetRequiredService<ITypesenseClient>();
        CollectionAliasResponse alias =
            await typesenseClient.RetrieveCollectionAlias(CollectionNames.EDUCATION_SEARCH, CancellationToken.None);

        return alias.CollectionName;
    }
}
