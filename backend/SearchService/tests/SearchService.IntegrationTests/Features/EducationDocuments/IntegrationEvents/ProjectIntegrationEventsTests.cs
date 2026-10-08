using Common;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;
using SearchService.IntegrationTests.Mocks;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.IntegrationTests.Features.EducationDocuments.IntegrationEvents;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ProjectIntegrationEventsTests : SearchServiceTestsBase
{
    public ProjectIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ProjectCreated_and_published_should_index_document()
    {
        Guid projectId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new ProjectCreated(projectId));

        EducationDocument? draft = await FindDocumentAsync(EducationDocument.CreateProjectId(projectId));
        Assert.NotNull(draft);
        Assert.Equal(EntityType.Project, draft.EntityType);
        Assert.Equal(EducationContentServiceClientMockExtensions.GetCourseIdForEntity(projectId), draft.CourseId);
        Assert.True(draft.IsDeleted);

        await InvokeMessageAndWaitAsync(new ProjectPublished(projectId));

        EducationDocument? published = await FindDocumentAsync(EducationDocument.CreateProjectId(projectId));
        Assert.NotNull(published);
        Assert.Equal("Project new", published.Title);
        Assert.False(published.IsDeleted);
    }

    [Fact]
    public async Task ProjectSoftDeleted_and_restored_should_toggle_is_deleted()
    {
        Guid projectId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new ProjectCreated(projectId));
        await InvokeMessageAndWaitAsync(new ProjectPublished(projectId));
        await InvokeMessageAndWaitAsync(new ProjectSoftDeleted(projectId));

        EducationDocument? deleted = await FindDocumentAsync(EducationDocument.CreateProjectId(projectId));
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);

        await InvokeMessageAndWaitAsync(new ProjectRestored(projectId));

        EducationDocument? restored = await FindDocumentAsync(EducationDocument.CreateProjectId(projectId));
        Assert.NotNull(restored);
        Assert.False(restored.IsDeleted);
    }
}
