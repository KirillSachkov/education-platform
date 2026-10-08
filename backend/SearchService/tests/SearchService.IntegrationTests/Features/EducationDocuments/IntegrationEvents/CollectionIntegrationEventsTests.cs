using Common;
using ContentAccess;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.IntegrationTests.Features.EducationDocuments.IntegrationEvents;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CollectionIntegrationEventsTests : SearchServiceTestsBase
{
    public CollectionIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CollectionCreated_should_index_document_with_access_tags()
    {
        Guid collectionId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        await SeedCollectionLookupAsync(BuildLookup(
            collectionId, PublicationStatus.PUBLISHED, [GrantTags.PUBLIC], title: "Подборка по C#"));

        await InvokeMessageAndWaitAsync(new CollectionCreated(collectionId, "PUBLIC", CourseId: null, authorId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));

        Assert.NotNull(document);
        Assert.Equal(EntityType.Collection, document.EntityType);
        Assert.Equal(collectionId, document.EntityId);
        Assert.Equal("Подборка по C#", document.Title);
        Assert.Contains(GrantTags.PUBLIC, document.RequiredAccessTags);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task CollectionPublished_should_make_draft_document_searchable()
    {
        // Точка хендлера — переход DRAFT→PUBLISHED (is_deleted: true→false).
        // Сначала кладём DRAFT-документ (is_deleted=true), затем публикуем.
        Guid collectionId = Guid.NewGuid();

        await SeedCollectionLookupAsync(BuildLookup(collectionId, PublicationStatus.DRAFT, [GrantTags.PUBLIC]));
        await InvokeMessageAndWaitAsync(new CollectionCreated(collectionId, "PUBLIC", CourseId: null, Guid.NewGuid()));

        EducationDocument? draft = await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));
        Assert.NotNull(draft);
        Assert.True(draft.IsDeleted);

        await SeedCollectionLookupAsync(BuildLookup(collectionId, PublicationStatus.PUBLISHED, [GrantTags.PUBLIC]));
        await InvokeMessageAndWaitAsync(new CollectionPublished(collectionId));

        EducationDocument? published = await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));
        Assert.NotNull(published);
        Assert.False(published.IsDeleted);
    }

    [Fact]
    public async Task CollectionAccessChanged_should_refresh_required_access_tags()
    {
        // Sync-critical: при смене AccessType подборки её required_access_tags в индексе
        // должны пересчитаться. Иначе платный контент остаётся под старым (публичным)
        // тегом и утекает в выдачу как доступный.
        Guid collectionId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        await SeedCollectionLookupAsync(BuildLookup(collectionId, PublicationStatus.PUBLISHED, [GrantTags.PUBLIC]));
        await InvokeMessageAndWaitAsync(new CollectionCreated(collectionId, "PUBLIC", courseId, authorId));

        EducationDocument? initial = await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));
        Assert.NotNull(initial);
        Assert.Contains(GrantTags.PUBLIC, initial.RequiredAccessTags);

        // Автор переводит подборку в ENROLLED — lookup теперь отдаёт plan-теги.
        await SeedCollectionLookupAsync(BuildLookup(
            collectionId,
            PublicationStatus.PUBLISHED,
            [GrantTags.PlanAll(), GrantTags.PlanCourse(courseId)]));

        await InvokeMessageAndWaitAsync(new CollectionAccessChanged(collectionId, "ENROLLED", courseId, authorId));

        EducationDocument? updated = await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));

        Assert.NotNull(updated);
        Assert.Contains(GrantTags.PlanAll(), updated.RequiredAccessTags);
        Assert.Contains(GrantTags.PlanCourse(courseId), updated.RequiredAccessTags);
        Assert.DoesNotContain(updated.RequiredAccessTags, tag => tag.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
        Assert.DoesNotContain(GrantTags.PUBLIC, updated.RequiredAccessTags);
    }

    [Fact]
    public async Task CollectionUpdated_with_draft_status_should_hide_document()
    {
        // Подборка, ушедшая в DRAFT, должна стать невидимой в поиске (is_deleted=true) —
        // фильтр выдачи режет is_deleted:=false.
        Guid collectionId = Guid.NewGuid();

        await SeedCollectionLookupAsync(BuildLookup(collectionId, PublicationStatus.PUBLISHED, [GrantTags.PUBLIC]));
        await InvokeMessageAndWaitAsync(new CollectionCreated(collectionId, "PUBLIC", CourseId: null, Guid.NewGuid()));

        EducationDocument? visible = await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));
        Assert.NotNull(visible);
        Assert.False(visible.IsDeleted);

        await SeedCollectionLookupAsync(BuildLookup(collectionId, PublicationStatus.DRAFT, [GrantTags.PUBLIC]));
        await InvokeMessageAndWaitAsync(new CollectionUpdated(collectionId));

        EducationDocument? hidden = await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));
        Assert.NotNull(hidden);
        Assert.True(hidden.IsDeleted);
    }

    [Fact]
    public async Task CollectionHardDeleted_should_remove_document_from_index()
    {
        Guid collectionId = Guid.NewGuid();

        await SeedCollectionLookupAsync(BuildLookup(collectionId, PublicationStatus.PUBLISHED, [GrantTags.PUBLIC]));
        await InvokeMessageAndWaitAsync(new CollectionCreated(collectionId, "PUBLIC", CourseId: null, Guid.NewGuid()));
        await InvokeMessageAndWaitAsync(new CollectionHardDeleted(collectionId));

        EducationDocument? document = await FindDocumentAsync(EducationDocument.CreateCollectionId(collectionId));

        Assert.Null(document);
    }

    private static CollectionSearchLookupDto BuildLookup(
        Guid collectionId,
        PublicationStatus status,
        IReadOnlyList<string> accessTags,
        string title = "Подборка") =>
        new(
            Id: collectionId,
            CourseId: null,
            CourseSlug: null,
            Title: title,
            Description: "Описание",
            ImageId: null,
            CourseTitle: null,
            Status: status,
            RequiredAccessTags: accessTags,
            UpdatedAt: DateTime.UtcNow);
}
