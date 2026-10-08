using ContentAccess;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.Materials.Queries;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class AuthorFreeMaterialsTests : EducationContentServiceTestsBase
{
    public AuthorFreeMaterialsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task FreeMaterials_Anonymous_ExcludesEnrolledAndDraft()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid publicId = await CreateMaterialAsync("Открытый", authorId, AccessType.PUBLIC, status: PublicationStatus.PUBLISHED, ct);
        Guid registeredId = await CreateMaterialAsync("Только зарегам", authorId, AccessType.REGISTERED, status: PublicationStatus.PUBLISHED, ct);
        await CreateMaterialAsync("Платный", authorId, AccessType.ENROLLED, status: PublicationStatus.PUBLISHED, ct);
        await CreateMaterialAsync("Черновик", authorId, AccessType.PUBLIC, status: PublicationStatus.DRAFT, ct);

        EntitlementChecker.DenyAll();
        EntitlementChecker.SetDecision(ResourceTypes.MATERIAL, publicId, AccessDecision.Granted(AccessReason.PUBLIC));
        EntitlementChecker.SetDecision(ResourceTypes.MATERIAL, registeredId, AccessDecision.Denied(AccessReason.NOT_AUTHENTICATED));

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/authors/{authorId}/free-materials?limit=10", ct);
        response.EnsureSuccessStatusCode();
        AuthorFreeMaterialsDto dto = await ReadResultAsync<AuthorFreeMaterialsDto>(response);

        Assert.Equal(2, dto.Items.Count);
        Assert.Contains(dto.Items, m => m.Id == publicId);
        Assert.Contains(dto.Items, m => m.Id == registeredId);
    }

    [Fact]
    public async Task FreeMaterials_LimitParam_Clamped()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        for (int i = 0; i < 5; i++)
        {
            await CreateMaterialAsync($"Материал {i}", authorId, AccessType.PUBLIC, status: PublicationStatus.PUBLISHED, ct);
        }

        EntitlementChecker.GrantAll();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/authors/{authorId}/free-materials?limit=3", ct);
        response.EnsureSuccessStatusCode();
        AuthorFreeMaterialsDto dto = await ReadResultAsync<AuthorFreeMaterialsDto>(response);

        Assert.Equal(3, dto.Items.Count);
    }

    [Fact]
    public async Task FreeMaterials_NoMaterials_ReturnsEmpty()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/authors/{authorId}/free-materials", ct);
        response.EnsureSuccessStatusCode();
        AuthorFreeMaterialsDto dto = await ReadResultAsync<AuthorFreeMaterialsDto>(response);

        Assert.Empty(dto.Items);
    }

    private async Task<Guid> CreateMaterialAsync(
        string title,
        Guid authorId,
        AccessType accessType,
        PublicationStatus status,
        CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            int boundCourseCount = accessType is AccessType.ENROLLED ? 1 : 0;
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.ARTICLE, accessType);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title}\n\nТело").Value,
                MaterialKind.ARTICLE,
                accessType,
                boundCourseCount,
                description: null);
            if (status == PublicationStatus.PUBLISHED)
            {
                Assert.True(material.Publish().IsSuccess);
            }

            db.Materials.Add(material);

            if (boundCourseCount > 0)
            {
                var course = new Course(
                    authorId,
                    Title.Create($"Курс для {title}").Value,
                    Description.Create("Описание").Value,
                    slug: CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value,
                    SortKey.Initial());
                Assert.True(course.Publish().IsSuccess);
                db.Courses.Add(course);
                db.CourseMaterials.Add(new CourseMaterial(course.Id, material.Id, SortKey.Initial()));
            }

            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }
}
