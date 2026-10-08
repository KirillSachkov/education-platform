using Common;
using ContentAccess;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;
using SearchService.IntegrationTests.Mocks;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.IntegrationTests.Features.EducationDocuments.IntegrationEvents;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CourseIntegrationEventsTests : SearchServiceTestsBase
{
    public CourseIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CourseCreated_and_published_should_index_document()
    {
        Guid courseId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, Guid.NewGuid()));

        EducationDocument? draft = await FindDocumentAsync(EducationDocument.CreateCourseId(courseId));
        Assert.NotNull(draft);
        Assert.Equal(EntityType.Course, draft.EntityType);
        Assert.Equal("Course title", draft.Title);
        Assert.True(draft.IsDeleted);

        await InvokeMessageAndWaitAsync(new CoursePublished(courseId));

        EducationDocument? published = await FindDocumentAsync(EducationDocument.CreateCourseId(courseId));
        Assert.NotNull(published);
        Assert.Equal("Course new", published.Title);
        Assert.Equal(EducationContentServiceClientMockExtensions.CourseUpdatedAtUtc.Ticks, published.UpdatedAtTicks);
        Assert.False(published.IsDeleted);
    }

    [Fact]
    public async Task CourseSoftDeleted_and_hard_deleted_should_update_index()
    {
        Guid courseId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new CourseCreated(courseId, Guid.NewGuid()));
        await InvokeMessageAndWaitAsync(new CoursePublished(courseId));
        await InvokeMessageAndWaitAsync(new CourseSoftDeleted(courseId));

        EducationDocument? deleted = await FindDocumentAsync(EducationDocument.CreateCourseId(courseId));
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);

        await InvokeMessageAndWaitAsync(new CourseHardDeleted(courseId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateCourseId(courseId));
        Assert.Null(document);
    }

    [Fact]
    public async Task CourseArchive_hides_child_materials_and_restore_shows_them()
    {
        // Issue #378: материал архивированного курса остаётся PUBLISHED, но должен исчезнуть
        // из выдачи (если этот курс — его единственный active-курс). Решение принимает ECS
        // lookup (IsCourseOrphaned), каскад в SearchService — только триггер пере-индекса.
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        // Материал изначально проиндексирован видимым (привязан к published-курсу).
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            materialId,
            "Cascade material",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: courseId,
            isDeleted: false));

        EducationDocument? before = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(before);
        Assert.False(before.IsDeleted);

        IEducationContentServiceClient mock = Services.GetRequiredService<IEducationContentServiceClient>();
        mock.SeedCourseMaterialIds(courseId, [materialId]);

        // Архив курса → ECS lookup материала теперь orphan (единственный курс архивирован).
        mock.SeedMaterialLookup(BuildMaterialLookup(materialId, courseId, authorId, isCourseOrphaned: true));
        await InvokeMessageAndWaitAsync(new CourseSoftDeleted(courseId));

        EducationDocument? hidden = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(hidden);
        Assert.True(hidden.IsDeleted);

        // Restore курса → lookup снова не-orphan → материал виден.
        mock.SeedMaterialLookup(BuildMaterialLookup(materialId, courseId, authorId, isCourseOrphaned: false));
        await InvokeMessageAndWaitAsync(new CourseRestored(courseId));

        EducationDocument? shown = await FindDocumentAsync(EducationDocument.CreateMaterialId(materialId));
        Assert.NotNull(shown);
        Assert.False(shown.IsDeleted);
    }

    private static MaterialSearchLookupDto BuildMaterialLookup(
        Guid materialId,
        Guid courseId,
        Guid authorId,
        bool isCourseOrphaned) =>
        new(
            materialId,
            courseId,
            "course-slug",
            "Cascade material",
            ImageId: null,
            ModuleId: null,
            CourseTitle: "Course",
            ModuleTitle: null,
            Status: PublicationStatus.PUBLISHED,
            RequiredAccessTags: [GrantTags.PUBLIC],
            UpdatedAt: DateTime.UtcNow,
            AuthorId: authorId,
            MaterialKind: "ARTICLE",
            IsCourseOrphaned: isCourseOrphaned);
}
