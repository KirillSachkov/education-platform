using System.Net;
using Common;
using ContentAccess;
using EducationContentService.Contracts;
using EducationContentService.Contracts.SearchExport;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using PlatformAuth.Authorization;
using DomainAccessType = EducationContentService.Domain.AccessType;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SearchExportTests : EducationContentServiceTestsBase
{
    public SearchExportTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ExportMaterials_ShouldReturnOnlyPublishedMaterialsWithCursorPagination()
    {
        // Issue #358: AccessType.FREE удалён, бесплатный доступ = REGISTERED.
        Guid courseId = await CreatePublishedCourseAsync("Backend Search", 1490m);
        Guid moduleId = await CreatePublishedModuleAsync("Материалы");
        Guid firstMaterialId = await CreateMaterialAsync("Registered material", "# free", DomainAccessType.REGISTERED, publish: true);
        Guid secondMaterialId = await CreateMaterialAsync("Paid material", "# paid", DomainAccessType.ENROLLED, publish: true);
        Guid draftMaterialId = await CreateMaterialAsync("Draft material", "# draft", DomainAccessType.PUBLIC, publish: false);

        await LinkModuleToCourseAsync(courseId, moduleId);
        await LinkMaterialToModuleAsync(moduleId, firstMaterialId);
        await LinkMaterialToModuleAsync(moduleId, secondMaterialId);
        await LinkMaterialToModuleAsync(moduleId, draftMaterialId);

        HttpResponseMessage firstResponse =
            await AppHttpClient.GetAsync("/internal/search/export/entities/material?limit=1");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        CursorResponse<SearchExportEntityDto> firstBatch =
            await ReadResultAsync<CursorResponse<SearchExportEntityDto>>(firstResponse);

        Assert.Single(firstBatch.Items);
        Assert.Equal(2, firstBatch.TotalCount);
        Assert.Equal(firstMaterialId, firstBatch.Items[0].EntityId);
        Assert.Equal(EntityType.Material, firstBatch.Items[0].EntityType);
        // Issue #358: REGISTERED material → один тег AUTHENTICATED, без plan:course/plan:all.
        IReadOnlyList<string> requiredTags = firstBatch.Items[0].RequiredAccessTags;
        Assert.Contains(GrantTags.AUTHENTICATED, requiredTags);
        Assert.DoesNotContain(requiredTags, t => t.StartsWith("course:", StringComparison.Ordinal));
        Assert.DoesNotContain(requiredTags, t => t.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));

        HttpResponseMessage secondResponse =
            await AppHttpClient.GetAsync(
                $"/internal/search/export/entities/material?cursor={firstBatch.NextCursor}&limit=10");

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        CursorResponse<SearchExportEntityDto> secondBatch =
            await ReadResultAsync<CursorResponse<SearchExportEntityDto>>(secondResponse);

        Assert.Single(secondBatch.Items);
        Assert.Equal(secondMaterialId, secondBatch.Items[0].EntityId);
        Assert.Equal(courseId, secondBatch.Items[0].CourseId);
        Assert.Equal(moduleId, secondBatch.Items[0].ModuleId);
        // Phase E/#599/#608: paid ENROLLED material → plan:all + plan:course
        // (без legacy course-tag).
        IReadOnlyList<string> secondTags = secondBatch.Items[0].RequiredAccessTags;
        Assert.Contains(GrantTags.PlanAll(), secondTags);
        Assert.Contains(GrantTags.PlanCourse(courseId), secondTags);
        Assert.DoesNotContain(secondTags, t => t.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
        Assert.DoesNotContain(secondTags, t => t.StartsWith("course:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAll_ShouldIncludePublishedCollectionInItemsTotalAndAccessTags()
    {
        Guid courseId = await CreatePublishedCourseAsync("Collection Search", 1490m);
        Guid publishedCollectionId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var published = new Collection(
                Guid.CreateVersion7(),
                Title.Create("Published collection").Value,
                courseId);
            Assert.True(published.Publish(hasAnyItem: true).IsSuccess);

            var draft = new Collection(
                Guid.CreateVersion7(),
                Title.Create("Draft collection").Value,
                courseId);

            db.Set<Collection>().AddRange(published, draft);
            await db.SaveChangesAsync();
            publishedCollectionId = published.Id;
        });

        HttpResponseMessage response =
            await AppHttpClient.GetAsync("/internal/search/export/entities?limit=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CursorResponse<SearchExportEntityDto> batch =
            await ReadResultAsync<CursorResponse<SearchExportEntityDto>>(response);

        Assert.Equal(2, batch.TotalCount); // one course + one published collection
        SearchExportEntityDto collection = Assert.Single(
            batch.Items,
            item => item.EntityType == EntityType.Collection);
        Assert.Equal(publishedCollectionId, collection.EntityId);
        Assert.Contains(GrantTags.PlanAll(), collection.RequiredAccessTags);
        Assert.Contains(GrantTags.PlanCourse(courseId), collection.RequiredAccessTags);
        Assert.DoesNotContain(GrantTags.PUBLIC, collection.RequiredAccessTags);
    }

    [Fact]
    public async Task ExportMaterials_AnonymousUser_ReturnsUnauthorized()
    {
        RemoveAuthentication();

        HttpResponseMessage response =
            await AppHttpClient.GetAsync("/internal/search/export/entities/material?limit=1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExportMaterials_ParticipantRole_ReturnsForbidden()
    {
        AuthenticateAs(Guid.CreateVersion7(), PlatformRoles.PARTICIPANT);

        HttpResponseMessage response =
            await AppHttpClient.GetAsync("/internal/search/export/entities/material?limit=1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreatePublishedCourseAsync(string title, decimal? price)
    {
        Guid courseId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var course = new Course(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create($"{title} description").Value,
                CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value, SortKey.Initial());

            Assert.True(course.Publish().IsSuccess);

            db.Courses.Add(course);
            await db.SaveChangesAsync();
            courseId = course.Id;
        });

        return courseId;
    }

    private async Task<Guid> CreatePublishedModuleAsync(string title)
    {
        Guid moduleId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var module = new Module(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create($"{title} description").Value);

            Assert.True(module.Publish().IsSuccess);

            db.Modules.Add(module);
            await db.SaveChangesAsync();
            moduleId = module.Id;
        });

        return moduleId;
    }

    private async Task<Guid> CreateMaterialAsync(
        string title,
        string content,
        DomainAccessType accessType,
        bool publish)
    {
        Guid materialId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var material = new Material(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                MaterialKind.ARTICLE,
                accessType);

            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create(content).Value,
                MaterialKind.ARTICLE,
                accessType,
                boundCourseCount: 1,
                description: null);

            if (publish)
            {
                Assert.True(material.Publish().IsSuccess);
            }

            db.Materials.Add(material);
            await db.SaveChangesAsync();
            materialId = material.Id;
        });

        return materialId;
    }

    private async Task LinkModuleToCourseAsync(Guid courseId, Guid moduleId)
    {
        await ExecuteInDb(async db =>
        {
            db.CourseItems.Add(new CourseItem(
                courseId,
                CourseItemType.Module,
                moduleId,
                SortKey.Initial(),
                isOptional: false));

            await db.SaveChangesAsync();
        });
    }

    private async Task LinkMaterialToModuleAsync(Guid moduleId, Guid materialId)
    {
        await ExecuteInDb(async db =>
        {
            db.ModuleItems.Add(new ModuleItem(
                moduleId,
                ModuleItemType.Material,
                materialId,
                SortKey.Initial(),
                isOptional: false));

            await db.SaveChangesAsync();
        });
    }
}
