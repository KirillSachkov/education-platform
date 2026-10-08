using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Modules;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using NSubstitute;
using Ordering;
using Shared.Messaging.IntegrationEvents.Education.Events;
using StackExchange.Redis;

namespace EducationContentService.IntegrationTests.Features.ProjectItems;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class IssueAccessMembershipTests : EducationContentServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public IssueAccessMembershipTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MembershipMutations_PublishAuthoritativeIssueAccessChanged()
    {
        MembershipFixture fixture = await SeedAsync(includeProjectBinding: false);
        AuthenticateAs(fixture.AuthorId, "platform-author");

        _factory.OutboxCollector.Clear();
        HttpResponseMessage attach = await AppHttpClient.PostAsync(
            $"/modules/{fixture.SourceModuleId}/issues/{fixture.IssueId}",
            content: null);
        Assert.Equal(HttpStatusCode.OK, attach.StatusCode);
        IssueAccessChanged attached = AssertIssueAccessChanged(fixture.IssueId);
        _factory.RedisDatabase.ClearReceivedCalls();
        await InvokeMessageAndWaitAsync(attached);
        await AssertWrittenTagsAsync(
            fixture.IssueId,
            expectedCourseId: fixture.SourceCourseId,
            excludedCourseId: fixture.TargetCourseId);

        _factory.OutboxCollector.Clear();
        HttpResponseMessage transfer = await AppHttpClient.PatchAsJsonAsync(
            $"/modules/{fixture.SourceModuleId}/items/{fixture.IssueId}/transfer",
            new TransferModuleItemRequest(fixture.TargetModuleId, null, null));
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);
        IssueAccessChanged transferred = AssertIssueAccessChanged(fixture.IssueId);
        _factory.RedisDatabase.ClearReceivedCalls();
        await InvokeMessageAndWaitAsync(transferred);
        await AssertWrittenTagsAsync(
            fixture.IssueId,
            expectedCourseId: fixture.TargetCourseId,
            excludedCourseId: fixture.SourceCourseId);

        _factory.OutboxCollector.Clear();
        HttpResponseMessage detachFromModule = await AppHttpClient.DeleteAsync(
            $"/modules/{fixture.TargetModuleId}/items/{fixture.IssueId}");
        Assert.Equal(HttpStatusCode.OK, detachFromModule.StatusCode);
        AssertIssueAccessChanged(fixture.IssueId);
    }

    [Fact]
    public async Task DeleteCourse_PublishesIssueAccessChangedForAffectedIssues()
    {
        MembershipFixture fixture = await SeedAsync(includeProjectBinding: true);
        AuthenticateAs(fixture.AuthorId, "platform-author");
        _factory.OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/courses/{fixture.SourceCourseId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertIssueAccessChanged(fixture.IssueId);

        MaterialAccessChanged materialAccess = Assert.Single(
            _factory.OutboxCollector.OfType<MaterialAccessChanged>());
        Assert.Equal(fixture.MaterialId, materialAccess.MaterialId);
        Assert.DoesNotContain(fixture.SourceCourseId, materialAccess.CourseIds);

        _factory.RedisDatabase.ClearReceivedCalls();
        await InvokeMessageAndWaitAsync(materialAccess);
        await _factory.RedisDatabase.Received(1).SetAddAsync(
            $"resource-access:material:{fixture.MaterialId:D}",
            Arg.Is<RedisValue[]>(values => values.All(
                value => value.ToString() != $"plan:course:{fixture.SourceCourseId:D}")),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DetachProjectFromCourse_PublishesIssueAccessChanged()
    {
        MembershipFixture fixture = await SeedAsync(includeProjectBinding: true);
        AuthenticateAs(fixture.AuthorId, "platform-author");
        _factory.OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/courses/{fixture.SourceCourseId}/items/{fixture.ProjectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertIssueAccessChanged(fixture.IssueId);
    }

    private IssueAccessChanged AssertIssueAccessChanged(Guid issueId)
    {
        IssueAccessChanged message = Assert.Single(_factory.OutboxCollector.OfType<IssueAccessChanged>());
        Assert.Equal(issueId, message.IssueId);
        return message;
    }

    private async Task AssertWrittenTagsAsync(Guid issueId, Guid expectedCourseId, Guid excludedCourseId)
    {
        await _factory.RedisDatabase.Received(1).SetAddAsync(
            $"resource-access:issue:{issueId:D}",
            Arg.Is<RedisValue[]>(values =>
                values.Any(value => value.ToString() == $"plan:course:{expectedCourseId:D}")
                && values.All(value => value.ToString() != $"plan:course:{excludedCourseId:D}")),
            Arg.Any<CommandFlags>());
    }

    private async Task<MembershipFixture> SeedAsync(bool includeProjectBinding)
    {
        return await ExecuteInDb(async db =>
        {
            Guid authorId = Guid.NewGuid();
            var sourceCourse = new Course(
                authorId,
                Title.Create("Source course").Value,
                Description.Create("Description").Value,
                CourseSlug.Create($"source-{Guid.NewGuid():N}").Value,
                SortKey.Initial());
            var targetCourse = new Course(
                authorId,
                Title.Create("Target course").Value,
                Description.Create("Description").Value,
                CourseSlug.Create($"target-{Guid.NewGuid():N}").Value,
                SortKey.Initial());
            var sourceModule = new Module(authorId, Title.Create("Source module").Value);
            var targetModule = new Module(authorId, Title.Create("Target module").Value);
            var project = new Project(authorId, Title.Create("Project").Value);
            var issue = new Issue(
                authorId,
                project.Id,
                Title.Create("Issue").Value,
                MarkdownContent.Create("Body").Value,
                AccessType.ENROLLED);
            var material = new Material(
                authorId,
                Title.Create("Course material").Value,
                MaterialKind.ARTICLE,
                AccessType.ENROLLED);

            db.Set<Course>().AddRange(sourceCourse, targetCourse);
            db.Set<Module>().AddRange(sourceModule, targetModule);
            db.Set<Project>().Add(project);
            db.Set<Issue>().Add(issue);
            db.Set<Material>().Add(material);
            db.Set<CourseMaterial>().Add(new CourseMaterial(
                sourceCourse.Id,
                material.Id,
                SortKey.Initial()));
            var courseItems = new List<CourseItem>
            {
                new CourseItem(
                    sourceCourse.Id,
                    CourseItemType.Module,
                    sourceModule.Id,
                    SortKey.Initial(),
                    isOptional: false),
                new CourseItem(
                    targetCourse.Id,
                    CourseItemType.Module,
                    targetModule.Id,
                    SortKey.Initial(),
                    isOptional: false),
            };
            if (includeProjectBinding)
            {
                courseItems.Add(new CourseItem(
                    sourceCourse.Id,
                    CourseItemType.Project,
                    project.Id,
                    SortKey.Initial(),
                    isOptional: false));
            }
            db.Set<CourseItem>().AddRange(courseItems);
            await db.SaveChangesAsync();

            return new MembershipFixture(
                authorId,
                sourceCourse.Id,
                targetCourse.Id,
                sourceModule.Id,
                targetModule.Id,
                project.Id,
                issue.Id,
                material.Id);
        });
    }

    private sealed record MembershipFixture(
        Guid AuthorId,
        Guid SourceCourseId,
        Guid TargetCourseId,
        Guid SourceModuleId,
        Guid TargetModuleId,
        Guid ProjectId,
        Guid IssueId,
        Guid MaterialId);
}
