using System.Net;
using System.Net.Http.Json;
using ContentAccess;
using EducationContentService.Contracts.Issues;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.Projects.ValueObjects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;

namespace EducationContentService.IntegrationTests.Features.ProjectItems;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class IssueInternalMaterialsSecurityTests : EducationContentServiceTestsBase
{
    public IssueInternalMaterialsSecurityTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Update_AllowsOwnDraftMaterial()
    {
        Guid authorId = Guid.NewGuid();
        Guid issueId = await CreatePublicIssueAsync(authorId);
        Guid materialId = await CreateMaterialAsync(authorId, published: false, AccessType.PUBLIC);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await UpdateAsync(issueId, materialId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_AllowsForeignPublishedPublicMaterial()
    {
        Guid authorId = Guid.NewGuid();
        Guid issueId = await CreatePublicIssueAsync(authorId);
        Guid materialId = await CreateMaterialAsync(Guid.NewGuid(), published: true, AccessType.PUBLIC);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await UpdateAsync(issueId, materialId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(false, "PUBLIC")]
    [InlineData(true, "ENROLLED")]
    public async Task Update_RejectsForeignMaterialOutsidePublicPublishedPolicy(
        bool published,
        string accessType)
    {
        Guid authorId = Guid.NewGuid();
        Guid issueId = await CreatePublicIssueAsync(authorId);
        Guid materialId = await CreateMaterialAsync(
            Guid.NewGuid(),
            published,
            Enum.Parse<AccessType>(accessType));
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await UpdateAsync(issueId, materialId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_RejectsMissingMaterial()
    {
        Guid authorId = Guid.NewGuid();
        Guid issueId = await CreatePublicIssueAsync(authorId);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await UpdateAsync(issueId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_AdminCanReferenceForeignDraft()
    {
        Guid issueAuthorId = Guid.NewGuid();
        Guid issueId = await CreatePublicIssueAsync(issueAuthorId);
        Guid materialId = await CreateMaterialAsync(Guid.NewGuid(), published: false, AccessType.PUBLIC);
        AuthenticateAsAdmin();

        HttpResponseMessage response = await UpdateAsync(issueId, materialId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Detail_HidesLegacyMissingAndForeignDraftReferences()
    {
        Guid issueAuthorId = Guid.NewGuid();
        Guid ownDraftId = await CreateMaterialAsync(issueAuthorId, published: false, AccessType.PUBLIC);
        Guid foreignDraftId = await CreateMaterialAsync(Guid.NewGuid(), published: false, AccessType.PUBLIC);
        Guid publicId = await CreateMaterialAsync(Guid.NewGuid(), published: true, AccessType.PUBLIC);
        Guid missingId = Guid.NewGuid();
        Guid issueId = await CreatePublicIssueAsync(
            issueAuthorId,
            [ownDraftId, foreignDraftId, publicId, missingId]);

        RemoveAuthentication();
        HttpResponseMessage anonymousResponse = await AppHttpClient.GetAsync($"/issues/{issueId}/detail");
        anonymousResponse.EnsureSuccessStatusCode();
        IssueDetailDto anonymousDetail = await ReadResultAsync<IssueDetailDto>(anonymousResponse);

        Assert.Equal([publicId], anonymousDetail.InternalMaterials.Select(x => x.ReferenceId));

        AuthenticateAs(issueAuthorId, "platform-author");
        HttpResponseMessage authorResponse = await AppHttpClient.GetAsync($"/issues/{issueId}/detail");
        authorResponse.EnsureSuccessStatusCode();
        IssueDetailDto authorDetail = await ReadResultAsync<IssueDetailDto>(authorResponse);

        Assert.Equal([ownDraftId, publicId], authorDetail.InternalMaterials.Select(x => x.ReferenceId));
    }

    [Fact]
    public async Task Detail_PublicDraft_IsVisibleOnlyToOwnerOrModerator()
    {
        Guid issueAuthorId = Guid.NewGuid();
        Guid issueId = await CreatePublicIssueAsync(issueAuthorId, publish: false);

        RemoveAuthentication();
        HttpResponseMessage anonymous = await AppHttpClient.GetAsync($"/issues/{issueId}/detail");
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        HttpResponseMessage foreignAuthor = await AppHttpClient.GetAsync($"/issues/{issueId}/detail");
        Assert.Equal(HttpStatusCode.NotFound, foreignAuthor.StatusCode);

        AuthenticateAs(issueAuthorId, "platform-author");
        HttpResponseMessage owner = await AppHttpClient.GetAsync($"/issues/{issueId}/detail");
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");
        HttpResponseMessage moderator = await AppHttpClient.GetAsync($"/issues/{issueId}/detail");
        Assert.Equal(HttpStatusCode.OK, moderator.StatusCode);
    }

    [Fact]
    public async Task Detail_EntitledStudent_SeesPublishedEnrolledMaterialOwnedByIssueAuthor()
    {
        Guid issueAuthorId = Guid.NewGuid();
        Guid materialId = await CreateMaterialAsync(issueAuthorId, published: true, AccessType.ENROLLED);
        Guid issueId = await CreatePublicIssueAsync(issueAuthorId, [materialId]);
        Guid studentId = Guid.NewGuid();
        AuthenticateAs(studentId, "platform-participant");
        EntitlementChecker.SetDecision(
            ResourceTypes.MATERIAL,
            materialId,
            AccessDecision.Granted(AccessReason.ENTITLEMENT));

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/issues/{issueId}/detail");

        response.EnsureSuccessStatusCode();
        IssueDetailDto detail = await ReadResultAsync<IssueDetailDto>(response);
        Assert.Equal([materialId], detail.InternalMaterials.Select(x => x.ReferenceId));
    }

    private Task<HttpResponseMessage> UpdateAsync(Guid issueId, Guid materialId) =>
        AppHttpClient.PutAsJsonAsync(
            $"/issues/{issueId}/internal-materials",
            new UpdateIssueInternalMaterialsRequest(
                [new InternalMaterialItem("Material", materialId, false)]));

    private async Task<Guid> CreatePublicIssueAsync(
        Guid authorId,
        IReadOnlyList<Guid>? internalMaterialIds = null,
        bool publish = true)
    {
        return await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Course-{Guid.NewGuid():N}").Value,
                Description.Create("Description").Value,
                CourseSlug.Create($"course-{Guid.NewGuid():N}").Value,
                SortKey.Initial());
            var project = new Project(authorId, Title.Create("Project").Value);
            var issue = new Issue(
                authorId,
                project.Id,
                Title.Create("Issue").Value,
                MarkdownContent.Create("Body").Value,
                AccessType.PUBLIC);
            if (publish)
                Assert.True(issue.Publish().IsSuccess);

            if (internalMaterialIds is not null)
            {
                issue.UpdateInternalMaterials(internalMaterialIds
                    .Select(id => IssueInternalMaterial.Create(ModuleItemType.Material, id, false).Value)
                    .ToArray());
            }

            db.Set<Course>().Add(course);
            db.Set<Project>().Add(project);
            db.Set<Issue>().Add(issue);
            db.Set<CourseItem>().Add(new CourseItem(
                course.Id,
                CourseItemType.Project,
                project.Id,
                SortKey.Initial(),
                isOptional: false));
            await db.SaveChangesAsync();
            return issue.Id;
        });
    }

    private async Task<Guid> CreateMaterialAsync(Guid authorId, bool published, AccessType accessType)
    {
        return await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create($"Material-{Guid.NewGuid():N}").Value,
                accessType: accessType);
            material.SetContent(MarkdownContent.Create("Body").Value);
            if (published)
                Assert.True(material.Publish().IsSuccess);

            db.Set<Material>().Add(material);
            await db.SaveChangesAsync();
            return material.Id;
        });
    }
}
