using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentAccess;
using EducationContentService.Contracts.Ownership;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Ownership;

/// <summary>
/// <c>GET /internal/ownership/{entityType}/{entityId}</c> — резолв владения сущностью для
/// нотификаций. Для материала возвращает <c>CreatedByUserId</c> = <c>materials.author_id</c>
/// (фактический создатель / помощник) дополнительно к автору курса (#400).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetEntityOwnershipTests : EducationContentServiceTestsBase
{
    public GetEntityOwnershipTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Material_CourseBound_ReturnsCourseAuthorAndMaterialCreator()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseAuthor = Guid.CreateVersion7();
        Guid materialCreator = Guid.CreateVersion7();   // помощник — отличается от автора курса

        Guid materialId = await CreateMaterialAsync(materialCreator, ct);
        Guid courseId = await CreateCourseAsync(courseAuthor, slug: "alpha", ct);
        await AttachMaterialToCourseAsync(courseId, materialId, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/ownership/{ResourceTypes.MATERIAL}/{materialId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EntityOwnershipDto dto = await ReadResultAsync<EntityOwnershipDto>(response);

        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(courseAuthor, dto.AuthorId);
        Assert.Equal(materialCreator, dto.CreatedByUserId);
    }

    [Fact]
    public async Task Material_InMultipleCourses_ReturnsEveryManager()
    {
        CancellationToken ct = CancellationToken.None;
        Guid materialCreator = Guid.CreateVersion7();
        Guid firstCourseAuthor = Guid.CreateVersion7();
        Guid secondCourseAuthor = Guid.CreateVersion7();

        Guid materialId = await CreateMaterialAsync(materialCreator, ct);
        Guid firstCourseId = await CreateCourseAsync(firstCourseAuthor, slug: "multi-alpha", ct);
        Guid secondCourseId = await CreateCourseAsync(secondCourseAuthor, slug: "multi-beta", ct);
        await AttachMaterialToCourseAsync(firstCourseId, materialId, ct);
        await AttachMaterialToCourseAsync(secondCourseId, materialId, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/ownership/{ResourceTypes.MATERIAL}/{materialId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Guid[] managers = payload.RootElement
            .GetProperty("result")
            .GetProperty("managerUserIds")
            .EnumerateArray()
            .Select(item => item.GetGuid())
            .ToArray();

        Assert.Equal(
            new[] { materialCreator, firstCourseAuthor, secondCourseAuthor }.Order(),
            managers.Order());
    }

    [Fact]
    public async Task Material_Orphan_ReturnsMaterialCreatorAndNullCourse()
    {
        CancellationToken ct = CancellationToken.None;
        Guid materialCreator = Guid.CreateVersion7();

        Guid materialId = await CreateMaterialAsync(materialCreator, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/ownership/{ResourceTypes.MATERIAL}/{materialId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EntityOwnershipDto dto = await ReadResultAsync<EntityOwnershipDto>(response);

        // Orphan: автор материала = создатель; CreatedByUserId совпадает с AuthorId.
        Assert.Null(dto.CourseId);
        Assert.Equal(materialCreator, dto.AuthorId);
        Assert.Equal(materialCreator, dto.CreatedByUserId);
    }

    [Fact]
    public async Task Quiz_CourseBound_ReturnsCourseAuthorAndQuizCreator()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseAuthor = Guid.CreateVersion7();
        Guid quizCreator = Guid.CreateVersion7();

        Guid quizId = await CreateQuizAsync(quizCreator, ct);
        Guid courseId = await CreateCourseAsync(courseAuthor, slug: "quiz-course", ct);
        await AttachQuizToCourseAsync(courseId, quizId, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/ownership/{ResourceTypes.QUIZ}/{quizId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EntityOwnershipDto dto = await ReadResultAsync<EntityOwnershipDto>(response);

        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(courseAuthor, dto.AuthorId);
        Assert.Equal(quizCreator, dto.CreatedByUserId);
        Assert.NotNull(dto.ManagerUserIds);
        Assert.Equal(new[] { courseAuthor, quizCreator }.Order(), dto.ManagerUserIds.Order());
    }

    [Fact]
    public async Task Quiz_Standalone_ReturnsQuizCreatorAndNullCourse()
    {
        CancellationToken ct = CancellationToken.None;
        Guid quizCreator = Guid.CreateVersion7();
        Guid quizId = await CreateQuizAsync(quizCreator, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/ownership/{ResourceTypes.QUIZ}/{quizId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EntityOwnershipDto dto = await ReadResultAsync<EntityOwnershipDto>(response);

        Assert.Null(dto.CourseId);
        Assert.Equal(quizCreator, dto.AuthorId);
        Assert.Equal(quizCreator, dto.CreatedByUserId);
        Assert.Equal([quizCreator], dto.ManagerUserIds);
    }

    [Fact]
    public async Task DatabaseContractV1_MatchesCanonicalOwnershipForEveryCommentTarget()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseAuthor = Guid.CreateVersion7();
        Guid collaborator = Guid.CreateVersion7();
        Guid courseId = await CreateCourseAsync(courseAuthor, "ownership-view", ct);
        Guid materialId = await CreateMaterialAsync(collaborator, ct);
        Guid quizId = await CreateQuizAsync(collaborator, ct);
        await AttachMaterialToCourseAsync(courseId, materialId, ct);
        await AttachQuizToCourseAsync(courseId, quizId, ct);

        Guid issueId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Guid projectId = await OwnershipTestSeed.CreateProjectAsync(
                db, collaborator, "ownership-view", ct);
            issueId = await OwnershipTestSeed.CreateIssueAsync(
                db, collaborator, projectId, "ownership-view", ct);
            await OwnershipTestSeed.AttachItemToCourseAsync(
                db, courseId, CourseItemType.Project, projectId, ct);
        });

        Assert.Equal(
            "education.comment_target_ownership_v1",
            EntityOwnershipDatabaseContract.COMMENT_TARGET_OWNERSHIP_VIEW_V1);
        foreach ((string entityType, Guid entityId) in new[]
                 {
                     (ResourceTypes.COURSE, courseId),
                     (ResourceTypes.MATERIAL, materialId),
                     (ResourceTypes.ISSUE, issueId),
                     (ResourceTypes.QUIZ, quizId),
                 })
        {
            Assert.Equal(courseAuthor, await ReadDatabaseContractOwnerAsync(entityType, entityId, ct));
        }
    }

    [Fact]
    public async Task Course_ReturnsNullCreatedByUserId()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseAuthor = Guid.CreateVersion7();

        Guid courseId = await CreateCourseAsync(courseAuthor, slug: "gamma", ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/ownership/{ResourceTypes.COURSE}/{courseId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EntityOwnershipDto dto = await ReadResultAsync<EntityOwnershipDto>(response);

        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(courseAuthor, dto.AuthorId);
        Assert.Null(dto.CreatedByUserId);
    }

    [Fact]
    public async Task DirectAuthorEntities_ReturnTheirCreator()
    {
        Guid authorId = Guid.CreateVersion7();
        Guid projectId = Guid.Empty;
        Guid moduleId = Guid.Empty;
        Guid collectionId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            projectId = await OwnershipTestSeed.CreateProjectAsync(db, authorId, "lookup");
            moduleId = await OwnershipTestSeed.CreateModuleAsync(db, authorId, "lookup");
            var collection = new Collection(authorId, Title.Create("Lookup collection").Value);
            db.Collections.Add(collection);
            await db.SaveChangesAsync();
            collectionId = collection.Id;
        });

        AuthenticateAsAdmin();

        foreach ((string entityType, Guid entityId) in new[]
                 {
                     ("project", projectId),
                     ("module", moduleId),
                     (ResourceTypes.COLLECTION, collectionId),
                 })
        {
            HttpResponseMessage response = await AppHttpClient.GetAsync(
                $"/internal/ownership/{entityType}/{entityId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            EntityOwnershipDto dto = await ReadResultAsync<EntityOwnershipDto>(response);
            Assert.Equal(authorId, dto.AuthorId);
            Assert.Equal(authorId, dto.CreatedByUserId);
        }
    }

    private async Task<Guid> CreateMaterialAsync(Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create("Урок").Value, MaterialKind.ARTICLE, AccessType.PUBLIC);
            material.Update(
                Title.Create("Урок").Value,
                MarkdownContent.Create("# Урок\n\nТело").Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC,
                boundCourseCount: 0,
                description: null);
            Assert.True(material.Publish().IsSuccess);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }

    private async Task<Guid> CreateCourseAsync(Guid authorId, string slug, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {slug}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create(slug).Value,
                SortKey.Initial());
            Assert.True(course.Publish().IsSuccess);
            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            id = course.Id;
        });
        return id;
    }

    private async Task<Guid> CreateQuizAsync(Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Quiz quiz = Quiz.Create(
                authorId,
                Title.Create("Квиз").Value,
                []).Value;
            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync(ct);
            id = quiz.Id;
        });
        return id;
    }

    private async Task AttachMaterialToCourseAsync(Guid courseId, Guid materialId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            db.CourseMaterials.Add(new CourseMaterial(courseId, materialId, SortKey.Initial()));
            await db.SaveChangesAsync(ct);
        });
    }

    private async Task AttachQuizToCourseAsync(Guid courseId, Guid quizId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            db.CourseQuizzes.Add(new CourseQuiz(courseId, quizId, SortKey.Initial()));
            await db.SaveChangesAsync(ct);
        });
    }

    private async Task<Guid> ReadDatabaseContractOwnerAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct)
    {
        Guid ownerId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            string sql = $$"""
                SELECT author_id AS "Value"
                FROM {{EntityOwnershipDatabaseContract.COMMENT_TARGET_OWNERSHIP_VIEW_V1}}
                WHERE target_entity_type = {0} AND target_entity_id = {1}
                """;
            ownerId = await db.Database.SqlQueryRaw<Guid>(sql, entityType, entityId).SingleAsync(ct);
        });
        return ownerId;
    }
}
