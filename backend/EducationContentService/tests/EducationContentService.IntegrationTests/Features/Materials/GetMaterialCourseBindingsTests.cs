using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using SharedKernel;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;

namespace EducationContentService.IntegrationTests.Features.Materials;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMaterialCourseBindingsTests : EducationContentServiceTestsBase
{
    public GetMaterialCourseBindingsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Endpoint_Anonymous_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/course-bindings",
            new GetMaterialCourseBindingsRequest(new[] { Guid.NewGuid() }));

        // SERVICE/ADMIN role requirement → anonymous is rejected.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {response.StatusCode}");
    }

    [Fact]
    public async Task Endpoint_ReturnsPrimaryCourseBindingPerMaterial()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid materialId = await CreateMaterialAsync("Материал", authorId, AccessType.PUBLIC, ct);
        Guid course1Id = await CreateCourseAsync(authorId, slug: "alpha", ct);
        Guid course2Id = await CreateCourseAsync(authorId, slug: "beta", ct);

        // Привязываем материал к двум курсам (alpha создан раньше → primary).
        // SQL сортирует по cm.id (Guid v7 ≈ time-ordered, но resolution = 1ms).
        // Между attach'ами ждём 5ms, чтобы две записи гарантированно попали
        // в разные миллисекунды и порядок был детерминирован.
        await AttachMaterialToCourseAsync(course1Id, materialId, ct);
        await Task.Delay(5, ct);
        await AttachMaterialToCourseAsync(course2Id, materialId, ct);

        AuthenticateAsAdmin();

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/course-bindings",
            new GetMaterialCourseBindingsRequest(new[] { materialId }));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<MaterialCourseBindingLookupDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<MaterialCourseBindingLookupDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);

        Assert.Single(envelope.Result);
        Assert.Equal(materialId, envelope.Result[0].MaterialId);
        Assert.Equal(course1Id, envelope.Result[0].CourseId);
        Assert.Equal("alpha", envelope.Result[0].CourseSlug);
    }

    [Fact]
    public async Task Endpoint_DraftCourse_NotReturned()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid materialId = await CreateMaterialAsync("Материал", authorId, AccessType.PUBLIC, ct);
        Guid draftCourseId = await CreateDraftCourseAsync(authorId, slug: "draft-course", ct);

        await AttachMaterialToCourseAsync(draftCourseId, materialId, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/course-bindings",
            new GetMaterialCourseBindingsRequest(new[] { materialId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<MaterialCourseBindingLookupDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<MaterialCourseBindingLookupDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result);
    }

    [Fact]
    public async Task Endpoint_StandaloneMaterial_NotInResponse()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreateMaterialAsync("Материал", authorId, AccessType.PUBLIC, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/course-bindings",
            new GetMaterialCourseBindingsRequest(new[] { materialId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<MaterialCourseBindingLookupDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<MaterialCourseBindingLookupDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result);
    }

    private async Task<Guid> CreateMaterialAsync(string title, Guid authorId, AccessType accessType, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.ARTICLE, accessType);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title}\n\nТело").Value,
                MaterialKind.ARTICLE,
                accessType,
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
                slug: CourseSlug.Create(slug).Value, SortKey.Initial());
            Assert.True(course.Publish().IsSuccess);
            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            id = course.Id;
        });
        return id;
    }

    private async Task<Guid> CreateDraftCourseAsync(Guid authorId, string slug, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {slug}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create(slug).Value, SortKey.Initial());
            // Без Publish — остаётся DRAFT.
            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            id = course.Id;
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
}
