using Common;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;
using SearchService.IntegrationTests.Mocks;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.IntegrationTests.Features.EducationDocuments.IntegrationEvents;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ModuleIntegrationEventsTests : SearchServiceTestsBase
{
    public ModuleIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ModuleCreated_should_index_document()
    {
        Guid moduleId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new ModuleCreated(moduleId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateModuleId(moduleId));
        Assert.NotNull(document);
        Assert.Equal(EntityType.Module, document.EntityType);
        Assert.Equal(moduleId, document.EntityId);
        Assert.Equal(EducationContentServiceClientMockExtensions.GetCourseIdForEntity(moduleId), document.CourseId);
        Assert.Equal("Module title", document.Title);
        Assert.Equal("Module description", document.Description);
        Assert.Equal(EducationContentServiceClientMockExtensions.ModuleCreatedAtUtc.Ticks, document.UpdatedAtTicks);
        Assert.True(document.IsDeleted);
    }

    [Fact]
    public async Task ModulePublished_should_make_document_searchable()
    {
        Guid moduleId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new ModuleCreated(moduleId));

        await InvokeMessageAndWaitAsync(new ModulePublished(moduleId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateModuleId(moduleId));
        Assert.NotNull(document);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task ModuleUpdated_should_reindex_document_with_updated_values()
    {
        Guid moduleId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new ModuleCreated(moduleId));
        await InvokeMessageAndWaitAsync(new ModuleUpdated(moduleId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateModuleId(moduleId));
        Assert.NotNull(document);
        Assert.Equal("Module new", document.Title);
        Assert.Equal("Description new", document.Description);
        Assert.Equal(EducationContentServiceClientMockExtensions.ModuleUpdatedAtUtc.Ticks, document.UpdatedAtTicks);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task ModuleSoftDeleted_and_restored_should_toggle_is_deleted()
    {
        Guid moduleId = Guid.NewGuid();
        await InvokeMessageAndWaitAsync(new ModuleCreated(moduleId));

        EducationDocument? beforeDelete = await FindDocumentAsync(EducationDocument.CreateModuleId(moduleId));
        Assert.NotNull(beforeDelete);
        long ticksBeforeDelete = beforeDelete.UpdatedAtTicks;

        await InvokeMessageAndWaitAsync(new ModuleSoftDeleted(moduleId));
        EducationDocument? deleted = await FindDocumentAsync(EducationDocument.CreateModuleId(moduleId));
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        Assert.True(deleted.UpdatedAtTicks >= ticksBeforeDelete);

        await InvokeMessageAndWaitAsync(new ModuleRestored(moduleId));
        EducationDocument? restored = await FindDocumentAsync(EducationDocument.CreateModuleId(moduleId));
        Assert.NotNull(restored);
        Assert.False(restored.IsDeleted);
    }

    [Fact]
    public async Task ModuleHardDeleted_should_remove_document_from_index()
    {
        Guid moduleId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new ModuleCreated(moduleId));

        await InvokeMessageAndWaitAsync(new ModuleHardDeleted(moduleId, []));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateModuleId(moduleId));
        Assert.Null(document);
    }
}
