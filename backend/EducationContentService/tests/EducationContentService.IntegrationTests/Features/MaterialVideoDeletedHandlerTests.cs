using EducationContentService.Core.Features.FileEvents;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Ordering;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class MaterialVideoDeletedHandlerTests : EducationContentServiceTestsBase
{
    private const long BindingRevision = 42;
    private readonly IntegrationTestsWebFactory _factory;

    public MaterialVideoDeletedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthoritativeLastVideoDeletion_MovesMaterialToDraftPublishesEventAndInvalidatesCourseCaches()
    {
        (Guid materialId, Guid courseId, Guid videoId) = await SeedPublishedVideoOnlyMaterialAsync();
        HybridCache cache = Services.GetRequiredService<HybridCache>();
        string[] cacheKeys = CourseCacheKeys(courseId);
        foreach (string key in cacheKeys)
            await cache.SetAsync(key, "stale");
        _factory.OutboxCollector.Clear();

        await InvokeHandlerAsync(new FileAssetDeleted(
            videoId,
            "video",
            "material_video",
            materialId,
            "material",
            BindingRevision));

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.AsNoTracking().SingleAsync(item => item.Id == materialId);
            Assert.Equal(PublicationStatus.DRAFT, material.Status);
            Assert.Null(material.VideoId);
        });
        MaterialSentToDraft sentToDraft = Assert.Single(_factory.OutboxCollector.OfType<MaterialSentToDraft>());
        Assert.Equal(materialId, sentToDraft.MaterialId);
        foreach (string key in cacheKeys)
            Assert.False(await IsCachedAsync(cache, key));
    }

    [Fact]
    public async Task StaleVideoDeletion_DoesNotChangeStatusPublishEventOrInvalidateCaches()
    {
        (Guid materialId, Guid courseId, Guid videoId) = await SeedPublishedVideoOnlyMaterialAsync();
        HybridCache cache = Services.GetRequiredService<HybridCache>();
        string cacheKey = $"curriculum:{courseId}:anon";
        await cache.SetAsync(cacheKey, "stale");
        _factory.OutboxCollector.Clear();

        await InvokeHandlerAsync(new FileAssetDeleted(
            videoId,
            "video",
            "material_video",
            materialId,
            "material",
            BindingRevision - 1));

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.AsNoTracking().SingleAsync(item => item.Id == materialId);
            Assert.Equal(PublicationStatus.PUBLISHED, material.Status);
            Assert.Equal(videoId, material.VideoId!.Value);
        });
        Assert.Empty(_factory.OutboxCollector.OfType<MaterialSentToDraft>());
        Assert.True(await IsCachedAsync(cache, cacheKey));
    }

    private async Task InvokeHandlerAsync(FileAssetDeleted message)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MaterialVideoDeletedHandler handler =
            ActivatorUtilities.CreateInstance<MaterialVideoDeletedHandler>(scope.ServiceProvider);
        await handler.Handle(message, CancellationToken.None);
    }

    private async Task<(Guid MaterialId, Guid CourseId, Guid VideoId)> SeedPublishedVideoOnlyMaterialAsync()
    {
        Guid authorId = Guid.CreateVersion7();
        Guid videoId = Guid.CreateVersion7();
        var course = new Course(
            authorId,
            Title.Create($"Курс {Guid.NewGuid():N}").Value,
            Description.Create("Описание тестового курса").Value,
            CourseSlug.Create($"course-{Guid.NewGuid():N}").Value,
            SortKey.Initial());
        var material = new Material(
            authorId,
            Title.Create($"Материал {Guid.NewGuid():N}").Value,
            MaterialKind.VIDEO,
            AccessType.PUBLIC);
        material.AttachVideo(VideoId.Create(videoId).Value, BindingRevision);
        Assert.True(material.Publish().IsSuccess);

        await ExecuteInDb(async db =>
        {
            db.Courses.Add(course);
            db.Materials.Add(material);
            db.CourseMaterials.Add(new CourseMaterial(course.Id, material.Id, SortKey.Initial()));
            await db.SaveChangesAsync();
        });

        return (material.Id, course.Id, videoId);
    }

    private static string[] CourseCacheKeys(Guid courseId) =>
    [
        $"curriculum:{courseId}:anon",
        $"curriculum:{courseId}:enrolled",
        $"curriculum:{courseId}:manage",
        $"course-landing:{courseId}:anon",
        $"course-landing:{courseId}:enrolled",
        $"course-landing:{courseId}:manage",
    ];

    private static async Task<bool> IsCachedAsync(HybridCache cache, string key)
    {
        bool factoryCalled = false;
        string? cached = await cache.GetOrCreateAsync<string?>(
            key,
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult<string?>(null);
            });

        if (factoryCalled)
            await cache.RemoveAsync(key);

        return !factoryCalled && cached is not null;
    }
}
