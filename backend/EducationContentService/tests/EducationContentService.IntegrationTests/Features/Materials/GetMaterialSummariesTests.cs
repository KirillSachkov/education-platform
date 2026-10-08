using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.Materials.UseCases;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Ordering;
using ProgressService.Contracts.HttpCommunication;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Materials;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMaterialSummariesTests : EducationContentServiceTestsBase
{
    public GetMaterialSummariesTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Endpoint_Anonymous_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/summaries",
            new GetMaterialSummariesRequest(new[] { Guid.NewGuid() }));

        // SERVICE/ADMIN role requirement → anonymous is rejected.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {response.StatusCode}");
    }

    [Fact]
    public async Task Endpoint_ParticipantRole_Returns403()
    {
        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/summaries",
            new GetMaterialSummariesRequest(new[] { Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_ServiceRole_ReturnsSummariesForExistingIds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid publicId = await CreateMaterialAsync("Публичный", authorId, AccessType.PUBLIC, ct);
        Guid enrolledId = await CreateMaterialAsync("Закрытый", authorId, AccessType.ENROLLED, ct);
        Guid missingId = Guid.CreateVersion7();

        AuthenticateAs(Guid.CreateVersion7(), "platform-service");

        IReadOnlyList<MaterialSummaryDto> result = await PostSummariesAsync(
            new[] { publicId, enrolledId, missingId });

        // Только существующие материалы попадают в ответ.
        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.Id == publicId);
        Assert.Contains(result, m => m.Id == enrolledId);
        Assert.DoesNotContain(result, m => m.Id == missingId);

        // Status + AccessType присутствуют — caller фильтрует по ним.
        MaterialSummaryDto pub = result.First(m => m.Id == publicId);
        Assert.Equal("PUBLIC", pub.AccessType);
        Assert.Equal("PUBLISHED", pub.Status);
        Assert.Equal(authorId, pub.AuthorId);
        Assert.Equal("Публичный", pub.Title);

        MaterialSummaryDto enr = result.First(m => m.Id == enrolledId);
        Assert.Equal("ENROLLED", enr.AccessType);
    }

    [Fact]
    public async Task Endpoint_ReturnsSummariesRegardlessOfStatus()
    {
        // S2S-эндпоинт не фильтрует по доступу/статусу — caller сам гейтит.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid draftId = await CreateDraftAsync("Черновик", authorId, ct);

        AuthenticateAsAdmin();

        IReadOnlyList<MaterialSummaryDto> result = await PostSummariesAsync(new[] { draftId });

        MaterialSummaryDto draft = Assert.Single(result);
        Assert.Equal(draftId, draft.Id);
        Assert.Equal("DRAFT", draft.Status);
    }

    [Fact]
    public async Task Endpoint_DoesNotLeakMarkdownBody_PreviewTruncated()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        // Тело > 280 символов — preview должен быть обрезан, тело целиком не утекает.
        string longBody = new string('я', 400);
        Guid materialId = await CreateMaterialWithBodyAsync("Длинный", authorId, longBody, ct);

        AuthenticateAsAdmin();

        IReadOnlyList<MaterialSummaryDto> result = await PostSummariesAsync(new[] { materialId });

        MaterialSummaryDto m = Assert.Single(result);
        Assert.NotNull(m.Preview);
        Assert.Equal(280, m.Preview!.Length);
    }

    [Fact]
    public async Task Endpoint_EnrichesViewsCount()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid materialId = await CreateMaterialAsync("С просмотрами", authorId, AccessType.PUBLIC, ct);

        var progressClient = Services.GetRequiredService<IProgressServiceClient>();
        progressClient.GetMaterialViewsCountsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, long>, Error>(
                new Dictionary<Guid, long> { [materialId] = 42L }));

        AuthenticateAsAdmin();

        IReadOnlyList<MaterialSummaryDto> result = await PostSummariesAsync(new[] { materialId });

        MaterialSummaryDto m = Assert.Single(result);
        Assert.Equal(42L, m.ViewsCount);
    }

    [Fact]
    public async Task Endpoint_BatchOverLimit_Returns400()
    {
        AuthenticateAsAdmin();

        var ids = Enumerable
            .Range(0, GetMaterialSummariesQueryValidator.MAX_BATCH_SIZE + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/summaries",
            new GetMaterialSummariesRequest(ids));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_EmptyIds_Returns400()
    {
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/summaries",
            new GetMaterialSummariesRequest(Array.Empty<Guid>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<IReadOnlyList<MaterialSummaryDto>> PostSummariesAsync(IReadOnlyCollection<Guid> ids)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/materials/summaries",
            new GetMaterialSummariesRequest(ids));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<MaterialSummaryDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<MaterialSummaryDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        return envelope.Result;
    }

    private async Task<Guid> CreateMaterialAsync(
        string title, Guid authorId, AccessType accessType, CancellationToken ct)
        => await CreateMaterialWithBodyAsync(title, authorId, $"# {title}\n\nТело", ct, accessType);

    private async Task<Guid> CreateMaterialWithBodyAsync(
        string title,
        Guid authorId,
        string body,
        CancellationToken ct,
        AccessType accessType = AccessType.PUBLIC)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            int boundCourseCount = accessType is AccessType.ENROLLED ? 1 : 0;
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.ARTICLE, accessType);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create(body).Value,
                MaterialKind.ARTICLE,
                accessType,
                boundCourseCount,
                description: null);
            Assert.True(material.Publish().IsSuccess);
            db.Materials.Add(material);

            if (accessType is AccessType.ENROLLED)
            {
                var course = new Course(
                    authorId,
                    Title.Create($"Курс для {title}").Value,
                    Description.Create("Описание").Value,
                    slug: CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value, SortKey.Initial());
                Assert.True(course.Publish().IsSuccess);
                db.Courses.Add(course);
                db.CourseMaterials.Add(new CourseMaterial(course.Id, material.Id, SortKey.Initial()));
            }

            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }

    private async Task<Guid> CreateDraftAsync(string title, Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.ARTICLE, AccessType.PUBLIC);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create("# draft").Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC,
                boundCourseCount: 0,
                description: null);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }
}
