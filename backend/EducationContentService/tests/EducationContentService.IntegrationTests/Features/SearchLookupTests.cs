using System.Net;
using ContentAccess;
using EducationContentService.Contracts.SearchLookup;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using PlatformAuth.Authorization;
using DomainAccessType = EducationContentService.Domain.AccessType;
using SearchLookupPublicationStatus = EducationContentService.Contracts.SearchLookup.PublicationStatus;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SearchLookupTests : EducationContentServiceTestsBase
{
    public SearchLookupTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetCourseSearchLookup_PublishedCourse_ReturnsTypedDto()
    {
        Guid courseId = await CreatePublishedCourseAsync("Search course", 1990m);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/courses/{courseId}");

        response.EnsureSuccessStatusCode();

        CourseSearchLookupDto dto = await ReadResultAsync<CourseSearchLookupDto>(response);
        Assert.Equal(courseId, dto.Id);
        Assert.Equal("Search course", dto.Title);
        Assert.Equal([GrantTags.PUBLIC], dto.RequiredAccessTags);
        Assert.Equal(SearchLookupPublicationStatus.PUBLISHED, dto.Status);
    }

    [Fact]
    public async Task GetMaterialSearchLookup_RegisteredWithCourse_EmitsAuthenticatedTag()
    {
        // Issue #358: AccessType.FREE удалён → REGISTERED (system default).
        // REGISTERED-материал получает один тег AUTHENTICATED — никаких plan-aliases.
        Guid authorId = Guid.NewGuid();
        Guid courseId = await CreatePublishedCourseAsync("Backend Search", 1490m);
        Guid moduleId = await CreatePublishedModuleAsync("Материалы");
        Guid materialId = await CreatePublishedMaterialAsync("Registered material", "# body", DomainAccessType.REGISTERED, authorId);

        await LinkModuleToCourseAsync(courseId, moduleId);
        await LinkMaterialToModuleAsync(moduleId, materialId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{materialId}");

        response.EnsureSuccessStatusCode();

        MaterialSearchLookupDto dto = await ReadResultAsync<MaterialSearchLookupDto>(response);
        Assert.Equal(materialId, dto.Id);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(moduleId, dto.ModuleId);
        Assert.Equal("Backend Search", dto.CourseTitle);
        Assert.Equal("Материалы", dto.ModuleTitle);
        Assert.Equal(
            new HashSet<string> { GrantTags.AUTHENTICATED },
            new HashSet<string>(dto.RequiredAccessTags, StringComparer.Ordinal));
        Assert.Equal(SearchLookupPublicationStatus.PUBLISHED, dto.Status);
    }

    [Fact]
    public async Task GetMaterialSearchLookup_EnrolledWithCourse_EmitsPlanAllAndPlanCourseOnly()
    {
        // Regression for #599/#186: ENROLLED material with a course must emit global plan:all
        // and course-specific plan:course:{id}, without author-scoped plan tags.
        Guid authorId = Guid.NewGuid();
        Guid courseId = await CreatePublishedCourseAsync("Paid course");
        Guid moduleId = await CreatePublishedModuleAsync("Платный модуль");
        Guid materialId = await CreatePublishedMaterialAsync("Paid material", "# body", DomainAccessType.ENROLLED, authorId);

        await LinkModuleToCourseAsync(courseId, moduleId);
        await LinkMaterialToModuleAsync(moduleId, materialId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{materialId}");

        response.EnsureSuccessStatusCode();

        MaterialSearchLookupDto dto = await ReadResultAsync<MaterialSearchLookupDto>(response);
        Assert.Equal(
            new HashSet<string>
            {
                GrantTags.PlanAll(),
                GrantTags.PlanCourse(courseId),
            },
            new HashSet<string>(dto.RequiredAccessTags, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetMaterialSearchLookup_ArchivedSoleCourse_MarksCourseOrphaned()
    {
        // Published material belongs only to an archived course; lookup consumers see it as orphaned.
        Guid courseId = await CreateArchivedCourseAsync("Archived course");
        Guid materialId = await CreatePublishedMaterialAsync("Orphaned material", "# body", DomainAccessType.REGISTERED);

        await LinkMaterialToCourseAsync(courseId, materialId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{materialId}");

        response.EnsureSuccessStatusCode();

        MaterialSearchLookupDto dto = await ReadResultAsync<MaterialSearchLookupDto>(response);
        Assert.True(dto.IsCourseOrphaned);
    }

    [Fact]
    public async Task GetMaterialSearchLookup_PublishedCourse_NotCourseOrphaned()
    {
        Guid courseId = await CreatePublishedCourseAsync("Active course");
        Guid materialId = await CreatePublishedMaterialAsync("Visible material", "# body", DomainAccessType.REGISTERED);

        await LinkMaterialToCourseAsync(courseId, materialId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{materialId}");

        response.EnsureSuccessStatusCode();

        MaterialSearchLookupDto dto = await ReadResultAsync<MaterialSearchLookupDto>(response);
        Assert.False(dto.IsCourseOrphaned);
        Assert.Equal(courseId, dto.CourseId);
    }

    [Fact]
    public async Task GetMaterialSearchLookup_NeverBoundMaterial_NotCourseOrphaned()
    {
        // #77: orphan без привязки к курсу остаётся видимым — is_course_orphaned=false.
        Guid materialId = await CreatePublishedMaterialAsync("Standalone material", "# body", DomainAccessType.PUBLIC);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{materialId}");

        response.EnsureSuccessStatusCode();

        MaterialSearchLookupDto dto = await ReadResultAsync<MaterialSearchLookupDto>(response);
        Assert.False(dto.IsCourseOrphaned);
        Assert.Null(dto.CourseId);
    }

    [Fact]
    public async Task GetMaterialSearchLookup_OnePublishedOneArchivedCourse_PrefersPublishedAndNotOrphaned()
    {
        // Материал в двух курсах: один архивирован, один активен → виден (есть active-курс),
        // и для отображения выбирается PUBLISHED-курс.
        Guid archivedCourseId = await CreateArchivedCourseAsync("Archived");
        Guid publishedCourseId = await CreatePublishedCourseAsync("Published");
        Guid materialId = await CreatePublishedMaterialAsync("Shared material", "# body", DomainAccessType.REGISTERED);

        await LinkMaterialToCourseAsync(archivedCourseId, materialId);
        await LinkMaterialToCourseAsync(publishedCourseId, materialId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{materialId}");

        response.EnsureSuccessStatusCode();

        MaterialSearchLookupDto dto = await ReadResultAsync<MaterialSearchLookupDto>(response);
        Assert.False(dto.IsCourseOrphaned);
        Assert.Equal(publishedCourseId, dto.CourseId);
    }

    [Fact]
    public async Task GetIssueSearchLookup_ReturnsProjectModuleAndCourseContext()
    {
        Guid courseId = await CreatePublishedCourseAsync("Algorithms", 990m);
        Guid projectId = await CreatePublishedProjectAsync("Project A");
        Guid moduleId = await CreatePublishedModuleAsync("Practice module");
        Guid issueId = await CreatePublishedIssueAsync(projectId, "Issue A", DomainAccessType.REGISTERED);

        await LinkProjectToCourseAsync(courseId, projectId);
        await LinkModuleToCourseAsync(courseId, moduleId);
        await LinkIssueToModuleAsync(moduleId, issueId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/issues/{issueId}");

        response.EnsureSuccessStatusCode();

        IssueSearchLookupDto dto = await ReadResultAsync<IssueSearchLookupDto>(response);
        Assert.Equal(issueId, dto.Id);
        Assert.Equal(projectId, dto.ProjectId);
        Assert.Equal(moduleId, dto.ModuleId);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal("Algorithms", dto.CourseTitle);
        Assert.Equal("Project A", dto.ProjectTitle);
        Assert.Equal("Practice module", dto.ModuleTitle);
        Assert.Equal([GrantTags.AUTHENTICATED], dto.RequiredAccessTags);
        Assert.Equal(SearchLookupPublicationStatus.PUBLISHED, dto.Status);
    }

    [Fact]
    public async Task GetModuleSearchLookup_UsesOnlyPublishedChildrenForAccessAggregation()
    {
        Guid courseId = await CreatePublishedCourseAsync("C# Course", 2990m);
        Guid moduleId = await CreatePublishedModuleAsync("Module 1");
        Guid draftIssueId = await CreateIssueAsync(Guid.CreateVersion7(), "Draft issue", DomainAccessType.ENROLLED, publish: false);

        await LinkModuleToCourseAsync(courseId, moduleId);
        await LinkIssueToModuleAsync(moduleId, draftIssueId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/modules/{moduleId}");

        response.EnsureSuccessStatusCode();

        ModuleSearchLookupDto dto = await ReadResultAsync<ModuleSearchLookupDto>(response);
        Assert.Equal(moduleId, dto.Id);
        Assert.Equal(courseId, dto.CourseId);
        // After Course.Price drop (2026-05-04): пустой список published-children →
        // BuildCoursePublicDefault() → PUBLIC. Раньше при price>0 был course-tag.
        Assert.Equal([GrantTags.PUBLIC], dto.RequiredAccessTags);
        Assert.Equal(SearchLookupPublicationStatus.PUBLISHED, dto.Status);
    }

    [Fact]
    public async Task GetProjectSearchLookup_UsesOnlyPublishedChildrenForAccessAggregation()
    {
        // Issue #358: AccessType.FREE удалён, бесплатный доступ = REGISTERED.
        // Project с REGISTERED child → один AUTHENTICATED-тег (без plan-aliases).
        Guid courseId = await CreatePublishedCourseAsync("Project course", 3990m);
        Guid projectId = await CreatePublishedProjectAsync("Project 1");
        Guid publishedRegisteredIssueId = await CreatePublishedIssueAsync(projectId, "Registered issue", DomainAccessType.REGISTERED);
        Guid draftPaidIssueId = await CreateIssueAsync(projectId, "Draft paid issue", DomainAccessType.ENROLLED, publish: false);

        await LinkProjectToCourseAsync(courseId, projectId);
        await LinkIssueToProjectAsync(projectId, publishedRegisteredIssueId);
        await LinkIssueToProjectAsync(projectId, draftPaidIssueId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/projects/{projectId}");

        response.EnsureSuccessStatusCode();

        ProjectSearchLookupDto dto = await ReadResultAsync<ProjectSearchLookupDto>(response);
        Assert.Equal(projectId, dto.Id);
        Assert.Equal(courseId, dto.CourseId);
        Assert.NotNull(dto.AuthorId);
        Assert.Equal(
            new HashSet<string> { GrantTags.AUTHENTICATED },
            new HashSet<string>(dto.RequiredAccessTags, StringComparer.Ordinal));
        Assert.Equal(SearchLookupPublicationStatus.PUBLISHED, dto.Status);
    }

    [Fact]
    public async Task GetMaterialSearchLookup_MissingMaterial_ReturnsNotFound()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialSearchLookup_AnonymousUser_ReturnsUnauthorized()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialSearchLookup_ParticipantRole_ReturnsForbidden()
    {
        AuthenticateAs(Guid.CreateVersion7(), PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/search/materials/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreatePublishedCourseAsync(string title, decimal? price = null)
    {
        _ = price;
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

    private async Task<Guid> CreatePublishedProjectAsync(string title)
    {
        Guid projectId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var project = new Project(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create($"{title} description").Value);

            Assert.True(project.Publish().IsSuccess);

            db.Projects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;
        });

        return projectId;
    }

    private async Task<Guid> CreatePublishedMaterialAsync(
        string title,
        string content,
        DomainAccessType accessType,
        Guid? authorId = null)
    {
        Guid materialId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId ?? Guid.NewGuid(),
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

            Assert.True(material.Publish().IsSuccess);

            db.Materials.Add(material);
            await db.SaveChangesAsync();
            materialId = material.Id;
        });

        return materialId;
    }

    private async Task<Guid> CreatePublishedIssueAsync(Guid projectId, string title, DomainAccessType accessType)
    {
        return await CreateIssueAsync(projectId, title, accessType, publish: true);
    }

    private async Task<Guid> CreateIssueAsync(Guid projectId, string title, DomainAccessType accessType, bool publish)
    {
        Guid issueId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var issue = new Issue(
                Guid.CreateVersion7(),
                projectId,
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title} content").Value,
                accessType);

            if (publish)
            {
                Assert.True(issue.Publish().IsSuccess);
            }

            db.Issues.Add(issue);
            await db.SaveChangesAsync();
            issueId = issue.Id;
        });

        return issueId;
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

    private async Task LinkProjectToCourseAsync(Guid courseId, Guid projectId)
    {
        await ExecuteInDb(async db =>
        {
            db.CourseItems.Add(new CourseItem(
                courseId,
                CourseItemType.Project,
                projectId,
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

    private async Task LinkIssueToModuleAsync(Guid moduleId, Guid issueId)
    {
        await ExecuteInDb(async db =>
        {
            db.ModuleItems.Add(new ModuleItem(
                moduleId,
                ModuleItemType.Issue,
                issueId,
                SortKey.Initial(),
                isOptional: false));

            await db.SaveChangesAsync();
        });
    }

    private async Task LinkIssueToProjectAsync(Guid projectId, Guid issueId)
    {
        await ExecuteInDb(async db =>
        {
            db.ProjectItems.Add(new ProjectItem(
                projectId,
                issueId,
                SortKey.Initial(),
                isOptional: false,
                maxScore: null));

            await db.SaveChangesAsync();
        });
    }

    private async Task LinkMaterialToCourseAsync(Guid courseId, Guid materialId)
    {
        await ExecuteInDb(async db =>
        {
            db.CourseMaterials.Add(new CourseMaterial(courseId, materialId, SortKey.Initial()));

            await db.SaveChangesAsync();
        });
    }

    private async Task<Guid> CreateArchivedCourseAsync(string title)
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
            Assert.True(course.Archive().IsSuccess);

            db.Courses.Add(course);
            await db.SaveChangesAsync();
            courseId = course.Id;
        });

        return courseId;
    }
}
