using Common;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;
using SearchService.IntegrationTests.Mocks;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.IntegrationTests.Features.EducationDocuments.IntegrationEvents;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class MaterialIntegrationEventsTests : SearchServiceTestsBase
{
    public MaterialIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task MaterialCreated_should_index_document()
    {
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new MaterialCreated(materialId, "PUBLIC", Guid.NewGuid()));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal(EntityType.Material, document.EntityType);
        Assert.Equal(materialId, document.EntityId);
        Assert.Equal(EducationContentServiceClientMockExtensions.GetCourseIdForEntity(materialId), document.CourseId);
        Assert.Equal(EducationContentServiceClientMockExtensions.GetModuleIdForEntity(materialId), document.ModuleId);
        Assert.Equal("Material title", document.Title);
        Assert.Equal(EducationContentServiceClientMockExtensions.MaterialCreatedAtUtc.Ticks, document.UpdatedAtTicks);
        Assert.True(document.IsDeleted);
    }

    [Fact]
    public async Task MaterialPublished_should_make_document_searchable()
    {
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new MaterialCreated(materialId, "PUBLIC", Guid.NewGuid()));
        await InvokeMessageAndWaitAsync(new MaterialPublished(materialId, "Material title", Guid.NewGuid(), [], NotifySubscribers: false));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task MaterialPublished_without_existing_document_should_index_document()
    {
        Guid materialId = Guid.NewGuid();
        await PrimeMaterialLookupAsync(materialId);

        await InvokeMessageAndWaitAsync(new MaterialPublished(materialId, "Material title", Guid.NewGuid(), [], NotifySubscribers: false));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal("Material new", document.Title);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task MaterialUpdated_without_existing_document_should_index_document()
    {
        Guid materialId = Guid.NewGuid();
        await PrimeMaterialLookupAsync(materialId);

        await InvokeMessageAndWaitAsync(new MaterialUpdated(materialId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(document);
        Assert.Equal("Material new", document.Title);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task MaterialUpdated_should_persist_chapter_titles_and_timestamps_on_existing_document()
    {
        // Regression for #180: live `MaterialUpdated` partial PATCH used to whitelist
        // every field except ChapterTitles/ChapterTimestamps, so timecodes generated
        // after the document was first indexed never reached Typesense and full-text
        // search by chapter never matched.
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new MaterialCreated(materialId, "PUBLIC", Guid.NewGuid()));

        EducationDocument? initial = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(initial);
        Assert.Empty(initial.ChapterTitles);
        Assert.Empty(initial.ChapterTimestamps);

        await InvokeMessageAndWaitAsync(new MaterialUpdated(materialId));

        EducationDocument? updated = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(updated);
        Assert.Equal(EducationContentServiceClientMockExtensions.MaterialUpdatedChapterTitles, updated.ChapterTitles);
        Assert.Equal(EducationContentServiceClientMockExtensions.MaterialUpdatedChapterTimestamps, updated.ChapterTimestamps);
    }

    [Fact]
    public async Task MaterialUpdated_should_persist_material_kind_on_existing_document()
    {
        // Regression for #286: live `MaterialUpdated` partial PATCH used to whitelist
        // every field except MaterialKind, so updates to a VIDEO material never
        // reached Typesense — frontend kept rendering the search hit as «Статья».
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new MaterialCreated(materialId, "PUBLIC", Guid.NewGuid()));

        EducationDocument? initial = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(initial);
        Assert.Equal("VIDEO", initial.MaterialKind);

        await InvokeMessageAndWaitAsync(new MaterialUpdated(materialId));

        EducationDocument? updated = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(updated);
        Assert.Equal("VIDEO", updated.MaterialKind);
    }

    [Fact]
    public async Task MaterialArchived_and_sent_to_draft_should_toggle_is_deleted()
    {
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new MaterialCreated(materialId, "PUBLIC", Guid.NewGuid()));
        await InvokeMessageAndWaitAsync(new MaterialPublished(materialId, "Material title", Guid.NewGuid(), [], NotifySubscribers: false));

        await InvokeMessageAndWaitAsync(new MaterialArchived(materialId));
        EducationDocument? archived = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(archived);
        Assert.True(archived.IsDeleted);

        await InvokeMessageAndWaitAsync(new MaterialSentToDraft(materialId));
        EducationDocument? draft = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(draft);
        Assert.True(draft.IsDeleted);
    }

    [Fact]
    public async Task MaterialHardDeleted_should_remove_document_from_index()
    {
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new MaterialCreated(materialId, "PUBLIC", Guid.NewGuid()));
        await InvokeMessageAndWaitAsync(new MaterialHardDeleted(materialId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.Null(document);
    }

    private async Task PrimeMaterialLookupAsync(Guid materialId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IEducationContentServiceClient educationService =
            scope.ServiceProvider.GetRequiredService<IEducationContentServiceClient>();

        await educationService.GetMaterialSearchLookupAsync(materialId, CancellationToken.None);
    }
}
