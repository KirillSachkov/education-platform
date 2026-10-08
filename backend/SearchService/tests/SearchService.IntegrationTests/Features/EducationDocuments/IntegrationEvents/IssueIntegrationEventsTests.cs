using Common;
using ContentAccess;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;
using SearchService.IntegrationTests.Mocks;
using SharedKernel;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.IntegrationTests.Features.EducationDocuments.IntegrationEvents;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class IssueIntegrationEventsTests : SearchServiceTestsBase
{
    public IssueIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task IssueCreated_should_index_document()
    {
        Guid issueId = Guid.NewGuid();
        Guid moduleId = EducationContentServiceClientMockExtensions.GetModuleIdForEntity(issueId);

        await InvokeMessageAndWaitAsync(new IssueCreated(issueId, moduleId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateIssueId(issueId));
        Assert.NotNull(document);
        Assert.Equal(EntityType.Issue, document.EntityType);
        Assert.Equal(issueId, document.EntityId);
        Assert.Equal(EducationContentServiceClientMockExtensions.GetCourseIdForEntity(issueId), document.CourseId);
        Assert.Equal(EducationContentServiceClientMockExtensions.GetProjectIdForEntity(issueId), document.ProjectId);
        Assert.Equal(moduleId, document.ModuleId);
        Assert.Equal("Issue title", document.Title);
        Assert.Equal(EducationContentServiceClientMockExtensions.IssueCreatedAtUtc.Ticks, document.UpdatedAtTicks);
        Assert.True(document.IsDeleted);
    }

    [Fact]
    public async Task IssuePublished_should_make_document_searchable()
    {
        Guid issueId = Guid.NewGuid();
        Guid moduleId = EducationContentServiceClientMockExtensions.GetModuleIdForEntity(issueId);

        await InvokeMessageAndWaitAsync(new IssueCreated(issueId, moduleId));
        await InvokeMessageAndWaitAsync(new IssuePublished(issueId, Guid.NewGuid()));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateIssueId(issueId));
        Assert.NotNull(document);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task IssueUpdated_should_reindex_document_with_updated_values()
    {
        Guid issueId = Guid.NewGuid();
        Guid moduleId = EducationContentServiceClientMockExtensions.GetModuleIdForEntity(issueId);

        await InvokeMessageAndWaitAsync(new IssueCreated(issueId, moduleId));

        await InvokeMessageAndWaitAsync(new IssueUpdated(issueId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateIssueId(issueId));
        Assert.NotNull(document);
        Assert.Equal("Issue new", document.Title);
        Assert.Equal(EducationContentServiceClientMockExtensions.GetCourseIdForEntity(issueId), document.CourseId);
        Assert.Equal(EducationContentServiceClientMockExtensions.IssueUpdatedAtUtc.Ticks, document.UpdatedAtTicks);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task IssueAccessChanged_should_refresh_access_tags_and_placement()
    {
        Guid issueId = Guid.CreateVersion7();
        Guid courseId = Guid.CreateVersion7();
        Guid projectId = Guid.CreateVersion7();
        Guid moduleId = Guid.CreateVersion7();

        await InvokeMessageAndWaitAsync(new IssueCreated(issueId, moduleId));

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IEducationContentServiceClient educationClient =
                scope.ServiceProvider.GetRequiredService<IEducationContentServiceClient>();
            educationClient
                .GetIssueSearchLookupAsync(issueId, Arg.Any<CancellationToken>())
                .Returns(Result.Success<IssueSearchLookupDto, Error>(new IssueSearchLookupDto(
                    issueId,
                    projectId,
                    courseId,
                    "new-course-slug",
                    "Access-updated issue",
                    moduleId,
                    "New course",
                    "New project",
                    "New module",
                    PublicationStatus.PUBLISHED,
                    [GrantTags.AUTHENTICATED],
                    DateTime.UtcNow)));
        }

        await InvokeMessageAndWaitAsync(new IssueAccessChanged(issueId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateIssueId(issueId));
        Assert.NotNull(document);
        Assert.Equal([GrantTags.AUTHENTICATED], document.RequiredAccessTags);
        Assert.Equal(courseId, document.CourseId);
        Assert.Equal("new-course-slug", document.CourseSlug);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task IssueSoftDeleted_and_restored_should_toggle_is_deleted()
    {
        Guid issueId = Guid.NewGuid();
        Guid moduleId = EducationContentServiceClientMockExtensions.GetModuleIdForEntity(issueId);

        await InvokeMessageAndWaitAsync(new IssueCreated(issueId, moduleId));

        EducationDocument? beforeDelete = await FindDocumentAsync(EducationDocument.CreateIssueId(issueId));
        Assert.NotNull(beforeDelete);
        long ticksBeforeDelete = beforeDelete.UpdatedAtTicks;

        await InvokeMessageAndWaitAsync(new IssueSoftDeleted(issueId, moduleId));
        EducationDocument? deleted = await FindDocumentAsync(EducationDocument.CreateIssueId(issueId));
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        Assert.True(deleted.UpdatedAtTicks >= ticksBeforeDelete);

        await InvokeMessageAndWaitAsync(new IssueRestored(issueId, moduleId));
        EducationDocument? restored = await FindDocumentAsync(EducationDocument.CreateIssueId(issueId));
        Assert.NotNull(restored);
        Assert.False(restored.IsDeleted);
    }

    [Fact]
    public async Task IssueHardDeleted_should_remove_document_from_index()
    {
        Guid issueId = Guid.NewGuid();
        Guid moduleId = EducationContentServiceClientMockExtensions.GetModuleIdForEntity(issueId);

        await InvokeMessageAndWaitAsync(new IssueCreated(issueId, moduleId));

        await InvokeMessageAndWaitAsync(new IssueHardDeleted(issueId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateIssueId(issueId));
        Assert.Null(document);
    }
}
