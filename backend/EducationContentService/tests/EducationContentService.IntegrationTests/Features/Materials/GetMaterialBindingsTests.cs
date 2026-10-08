using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Materials;

/// <summary>
///     С #500 endpoint открыт для анонимов — блок «Содержится в» на странице материала
///     виден каждому читателю. Проверяем anon-доступ и PUBLISHED-only фильтрацию.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMaterialBindingsTests : EducationContentServiceTestsBase
{
    public GetMaterialBindingsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Bindings_Anonymous_ReturnsPublishedCourseOnly()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid materialId = await CreateMaterialAsync("Материал", authorId, ct);
        Guid publishedCourseId = await CreateCourseAsync(authorId, slug: "pub-course", publish: true, ct);
        Guid draftCourseId = await CreateCourseAsync(authorId, slug: "draft-course", publish: false, ct);

        await AttachMaterialToCourseAsync(publishedCourseId, materialId, ct);
        await AttachMaterialToCourseAsync(draftCourseId, materialId, ct);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/bindings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<MaterialBindingsDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<MaterialBindingsDto>>();
        Assert.NotNull(envelope?.Result);

        MaterialCourseBindingDto course = Assert.Single(envelope.Result.Courses);
        Assert.Equal(publishedCourseId, course.CourseId);
        Assert.Equal("pub-course", course.Slug);
        Assert.Empty(envelope.Result.Collections);
    }

    private async Task<Guid> CreateMaterialAsync(string title, Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.ARTICLE, AccessType.PUBLIC);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title}\n\nТело").Value,
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

    private async Task<Guid> CreateCourseAsync(Guid authorId, string slug, bool publish, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {slug}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create(slug).Value, SortKey.Initial());
            if (publish)
            {
                Assert.True(course.Publish().IsSuccess);
            }
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
