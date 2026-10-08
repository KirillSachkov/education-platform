using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Collections;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class CollectionTests : EducationContentServiceTestsBase
{
    public CollectionTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task AddSection_ToFreshCollection_SucceedsAndPersistsSection()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);

        HttpResponseMessage addResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections",
            new AddSectionRequest(null, null),
            ct);

        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
        await ExecuteInDb(async db =>
        {
            CollectionSection? section = await db.Set<CollectionSection>()
                .FirstOrDefaultAsync(s => s.CollectionId == collectionId, ct);
            Assert.NotNull(section);
        });
    }

    [Fact]
    public async Task AddItem_ToFreshSection_SucceedsAndPersistsItem()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);

        HttpResponseMessage sectionResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections",
            new AddSectionRequest(null, null),
            ct);
        sectionResp.EnsureSuccessStatusCode();
        Guid sectionId = await ReadResultAsync<Guid>(sectionResp);

        Guid materialId = await CreateMaterialInDb(ct);

        HttpResponseMessage itemResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(materialId),
            ct);

        Assert.Equal(HttpStatusCode.OK, itemResp.StatusCode);
        await ExecuteInDb(async db =>
        {
            CollectionItem? item = await db.Set<CollectionItem>()
                .FirstOrDefaultAsync(i => i.SectionId == sectionId, ct);
            Assert.NotNull(item);
            Assert.Equal(materialId, item.ReferenceId);
            Assert.Equal(CollectionItemType.MATERIAL, item.ItemType);
        });
    }

    [Fact]
    public async Task Create_SpaceLevel_DefaultAccessTypeIsPublic()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);

        await ExecuteInDb(async db =>
        {
            Collection? c = await db.Set<Collection>().FirstOrDefaultAsync(x => x.Id == collectionId, ct);
            Assert.NotNull(c);
            Assert.Equal(AccessType.PUBLIC, c.AccessType);
            Assert.Null(c.CourseId);
        });
    }

    [Fact]
    public async Task Create_CourseLevel_DefaultAccessTypeIsEnrolled()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);

        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Course collection", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        await ExecuteInDb(async db =>
        {
            Collection? c = await db.Set<Collection>().FirstOrDefaultAsync(x => x.Id == collectionId, ct);
            Assert.NotNull(c);
            Assert.Equal(AccessType.ENROLLED, c.AccessType);
            Assert.Equal(courseId, c.CourseId);
        });
    }

    [Fact]
    public async Task Create_SpaceLevel_WithEnrolledAccessType_Succeeds_AfterPlanBoundRefactor()
    {
        // После #77: orphan ENROLLED легитимен (гейт через платформенный plan:all).
        CancellationToken ct = CancellationToken.None;

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Orphan ENROLLED", null, CourseId: null, AccessType: "ENROLLED"),
            ct);

        resp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(resp);

        await ExecuteInDb(async db =>
        {
            Collection? c = await db.Set<Collection>().FirstOrDefaultAsync(x => x.Id == collectionId, ct);
            Assert.NotNull(c);
            Assert.Equal(AccessType.ENROLLED, c.AccessType);
            Assert.Null(c.CourseId);
        });
    }

    [Fact]
    public async Task Update_SpaceLevel_ToRegisteredAccessType_Succeeds_AfterPlanBoundRefactor()
    {
        // Issue #358: AccessType.FREE удалён, его роль закрыта REGISTERED.
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);

        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            $"/collections/{collectionId}",
            new UpdateCollectionRequest("Name", null, AccessType: "REGISTERED"),
            ct);

        resp.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Collection? c = await db.Set<Collection>().FirstOrDefaultAsync(x => x.Id == collectionId, ct);
            Assert.NotNull(c);
            Assert.Equal(AccessType.REGISTERED, c.AccessType);
        });
    }

    [Fact]
    public async Task Update_CourseLevel_ToRegistered_Succeeds()
    {
        // Issue #358: AccessType.FREE удалён.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        HttpResponseMessage updateResp = await AppHttpClient.PutAsJsonAsync(
            $"/collections/{collectionId}",
            new UpdateCollectionRequest("CL", null, AccessType: "REGISTERED"),
            ct);

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        await ExecuteInDb(async db =>
        {
            Collection? c = await db.Set<Collection>().FirstOrDefaultAsync(x => x.Id == collectionId, ct);
            Assert.NotNull(c);
            Assert.Equal(AccessType.REGISTERED, c.AccessType);
        });
    }

    [Fact]
    public async Task Detail_Public_Anonymous_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct); // PUBLIC by default
        await PublishCollection(collectionId, ct);

        RemoveAuthentication();
        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Detail_EnrolledCollection_Anonymous_Returns200_WithHeaderLocked()
    {
        // С 2026-04-25: detail PUBLISHED-подборки больше не all-or-nothing. Анониму
        // отдаём структуру (sections + items) — markdown body не утекает (preview=NULL),
        // header помечен `isAccessible=false, lockReason=anonymous`. Это нужно, чтобы
        // PUBLIC материалы внутри гейтнутой подборки оставались видимыми по прямой ссылке.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);
        await PublishCollection(collectionId, ct);

        RemoveAuthentication();

        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadResultAsync<CollectionDetailDto>(resp);
        Assert.NotNull(body);
        Assert.False(body.IsAccessible);
        Assert.Equal("anonymous", body.LockReason);
    }

    [Fact]
    public async Task Detail_EnrolledCollection_AuthenticatedWithoutGrant_Returns200_WithHeaderLocked()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);
        await PublishCollection(collectionId, ct);

        AuthenticateAs(Guid.NewGuid(), "platform-student");

        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadResultAsync<CollectionDetailDto>(resp);
        Assert.NotNull(body);
        Assert.False(body.IsAccessible);
        // Phase E: ENROLLED-collection требует plan-grant. Залогинен без грантов
        // → lockReason = plan_required (CTA на /pricing).
        Assert.Equal("plan_required", body.LockReason);
    }

    [Fact]
    public async Task Detail_EnrolledCollection_AuthenticatedWithGrant_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);
        await PublishCollection(collectionId, ct);

        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-student");

        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task List_AuthorCollections_Anonymous_MarksInaccessibleWithLockReason()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        // Подборка курса с ENROLLED access_type — авторство принадлежит текущему admin-юзеру
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Locked", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);
        await PublishCollection(collectionId, ct);

        Guid authorId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Collection c = await db.Set<Collection>().FirstAsync(x => x.Id == collectionId, ct);
            authorId = c.AuthorId;
        });

        RemoveAuthentication();
        HttpResponseMessage listResp = await AppHttpClient.GetAsync(
            $"/authors/{authorId}/collections?limit=20", ct);
        listResp.EnsureSuccessStatusCode();

        var body = await ReadResultAsync<CursorResponse<CollectionSummaryDto>>(listResp);

        Assert.NotNull(body);
        CollectionSummaryDto? target = body.Items.FirstOrDefault(i => i.Id == collectionId);
        Assert.NotNull(target);
        Assert.False(target.IsAccessible);
        Assert.Equal("anonymous", target.LockReason);
    }

    [Fact]
    public async Task Update_EmptyAccessType_Returns400()
    {
        // Validator: NotEmpty перед Must(Enum.TryParse).
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);

        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            $"/collections/{collectionId}",
            new UpdateCollectionRequest("T", null, AccessType: ""),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Detail_DraftCollection_Author_ReturnsOwn()
    {
        // Автор должен видеть собственные DRAFT подборки через detail (для editor'а).
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);
        // DRAFT by default — не публикуем.

        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Detail_DraftCollection_OtherUser_Returns404()
    {
        // Чужая DRAFT подборка должна скрываться (SQL-предикат author_id) → 404,
        // а не 401/403 — так detail-эндпоинт не палит существование чернового ресурса.
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);

        AuthenticateAs(Guid.NewGuid(), "platform-student");
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Detail_EnrolledCollection_Admin_Returns200_WithoutGrant()
    {
        // FakeEntitlementChecker.DenyAll() должен НЕ затрагивать админа —
        // IsAdmin subject обходит Redis в RealEntitlementChecker. Имитируем через Fake
        // (у нас admin_id токен = base.AuthenticateAsAdmin(), проверяем что DenyAll
        // не ломает админу detail на ENROLLED).
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);
        await PublishCollection(collectionId, ct);

        // Remain authenticated as admin (InitializeAsync), then DenyAll:
        EntitlementChecker.DenyAll();
        // Admin всё равно автор подборки в этом тесте — проверка проходит по
        // isAuthor short-circuit'у, не затрагивая Redis. Это подтверждает, что
        // автор админ одновременно получает доступ.
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteCourse_DropsCourseIdButKeepsEnrolledAccessType()
    {
        // После #77: при удалении курса Collection.OnCourseDetached обнуляет CourseId,
        // но AccessType больше НЕ даунгрейдится — orphan ENROLLED легитимен и продолжает
        // гейтиться через платформенный plan:all.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);

        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        // Admin is author of the course (AuthenticateAsAdmin) — allow delete via ownership.
        HttpResponseMessage deleteResp = await AppHttpClient.DeleteAsync(
            $"/courses/{courseId}", ct);
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        await ExecuteInDb(async db =>
        {
            Collection? c = await db.Set<Collection>().FirstOrDefaultAsync(x => x.Id == collectionId, ct);
            Assert.NotNull(c);
            Assert.Equal(AccessType.ENROLLED, c.AccessType);
            Assert.Null(c.CourseId);
        });
    }

    [Fact]
    public async Task DeleteCourse_LeavesPublicCollectionsUntouched_ExceptCourseId()
    {
        // Для PUBLIC подборок OnCourseDetached — no-op (aggregate не мутируется),
        // CourseId обнуляет FK SetNull на уровне БД. AccessType остаётся PUBLIC.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);

        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Public", null, courseId, AccessType: "REGISTERED"),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        HttpResponseMessage deleteResp = await AppHttpClient.DeleteAsync(
            $"/courses/{courseId}", ct);
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        await ExecuteInDb(async db =>
        {
            Collection? c = await db.Set<Collection>().FirstOrDefaultAsync(x => x.Id == collectionId, ct);
            Assert.NotNull(c);
            Assert.Equal(AccessType.REGISTERED, c.AccessType);
            Assert.Null(c.CourseId); // FK SetNull
        });
    }

    private async Task<Guid> CreateCollectionViaApi(CancellationToken ct)
    {
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Test", null, null),
            ct);
        createResp.EnsureSuccessStatusCode();
        return await ReadResultAsync<Guid>(createResp);
    }

    [Fact]
    public async Task List_EnrolledCollectionWithPublicItem_Anonymous_CardUnlocked()
    {
        // Главный пользовательский сценарий: автор завёл ENROLLED-подборку, добавил
        // в неё PUBLIC материал. На карточке (LIST) для анонима замок снимается,
        // потому что хотя бы один материал доступен. Анон может кликнуть и увидеть
        // структуру с per-item замками.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId), // ENROLLED by default
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        // PUBLISHED PUBLIC material — добавляем в подборку.
        Guid publicMaterialId = await CreatePublishedMaterialInDb(AccessType.PUBLIC, ct);
        await AddItemToCollection(collectionId, publicMaterialId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        Guid authorId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Collection c = await db.Set<Collection>().FirstAsync(x => x.Id == collectionId, ct);
            authorId = c.AuthorId;
        });

        RemoveAuthentication();
        HttpResponseMessage listResp = await AppHttpClient.GetAsync(
            $"/authors/{authorId}/collections?limit=20", ct);
        listResp.EnsureSuccessStatusCode();

        var body = await ReadResultAsync<CursorResponse<CollectionSummaryDto>>(listResp);
        Assert.NotNull(body);
        CollectionSummaryDto? target = body.Items.FirstOrDefault(i => i.Id == collectionId);
        Assert.NotNull(target);
        Assert.True(target.IsAccessible, "Card должна быть unlocked: внутри есть PUBLIC материал.");
        Assert.Null(target.LockReason);
    }

    [Fact]
    public async Task List_EnrolledCollectionWithOnlyEnrolledItems_Anonymous_CardLocked()
    {
        // Контр-кейс: подборка ENROLLED, внутри только ENROLLED материалы, привязанные
        // к курсу. Аноним → карточка locked, lockReason=anonymous.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        Guid enrolledMaterialId = await CreatePublishedMaterialInDb(AccessType.ENROLLED, ct, courseId);
        await AddItemToCollection(collectionId, enrolledMaterialId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        Guid authorId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Collection c = await db.Set<Collection>().FirstAsync(x => x.Id == collectionId, ct);
            authorId = c.AuthorId;
        });

        RemoveAuthentication();
        HttpResponseMessage listResp = await AppHttpClient.GetAsync(
            $"/authors/{authorId}/collections?limit=20", ct);
        listResp.EnsureSuccessStatusCode();

        var body = await ReadResultAsync<CursorResponse<CollectionSummaryDto>>(listResp);
        Assert.NotNull(body);
        CollectionSummaryDto? target = body.Items.FirstOrDefault(i => i.Id == collectionId);
        Assert.NotNull(target);
        Assert.False(target.IsAccessible, "Card должна быть locked: внутри только ENROLLED материалы.");
        Assert.Equal("anonymous", target.LockReason);
    }

    [Fact]
    public async Task Detail_EnrolledCollectionWithMixedItems_AuthenticatedNoEnrollment_PerItemLocks()
    {
        // Залогиненный без grant'ов на курс: header → not_enrolled, PUBLIC item → доступен,
        // ENROLLED item → not_enrolled (а не anonymous, как у анона). Проверяем что lockReason
        // правильно различает анона и залогиненного.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        Guid publicId = await CreatePublishedMaterialInDb(AccessType.PUBLIC, ct);
        Guid enrolledId = await CreatePublishedMaterialInDb(AccessType.ENROLLED, ct, courseId);
        await AddItemToCollection(collectionId, publicId, ct);
        await AddItemToCollection(collectionId, enrolledId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        // Логинимся как студент без grant'ов (FakeEntitlementChecker → пустые user grants).
        AuthenticateAs(Guid.NewGuid(), "platform-student");

        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var detail = await ReadResultAsync<CollectionDetailDto>(resp);
        Assert.NotNull(detail);
        Assert.False(detail.IsAccessible);
        // Phase E: ENROLLED → plan_required (нужен план).
        Assert.Equal("plan_required", detail.LockReason);

        var items = detail.Sections.SelectMany(s => s.Items).ToList();
        CollectionItemDto publicItem = items.Single(i => i.ReferenceId == publicId);
        CollectionItemDto enrolledItem = items.Single(i => i.ReferenceId == enrolledId);

        Assert.True(publicItem.IsAccessible, "PUBLIC item остаётся доступным.");
        Assert.Null(publicItem.LockReason);
        Assert.False(enrolledItem.IsAccessible);
        Assert.Equal("plan_required", enrolledItem.LockReason);
    }

    [Fact]
    public async Task Detail_EnrolledCollectionWithCrossAuthorEnrolledItem_CollectionAuthor_ItemStaysLocked()
    {
        // Регрессия на security-finding: collection author НЕ должен видеть как accessible
        // материалы ДРУГИХ авторов внутри своей подборки. Только material-detail имеет право
        // выдавать или скрывать тело — collection-detail должен честно сказать «locked».
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        // Материал создаётся под отдельным authorId внутри CreatePublishedMaterialInDb
        // (он использует Guid.NewGuid()). Подборка же принадлежит текущему admin-юзеру
        // из InitializeAsync. Этого достаточно: collection.AuthorId != material.AuthorId.
        Guid otherAuthorEnrolledId = await CreatePublishedMaterialInDb(
            AccessType.ENROLLED, ct, courseId);
        await AddItemToCollection(collectionId, otherAuthorEnrolledId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        // Текущий юзер админ, поэтому он сначала пройдёт через admin-bypass и увидит
        // accessible=true. Чтобы изолировать collection-author logic, понизим юзера до
        // student с тем же UserId, что и collection.AuthorId. Идентифицируем authorId:
        Guid collectionAuthorId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Collection c = await db.Set<Collection>().FirstAsync(x => x.Id == collectionId, ct);
            collectionAuthorId = c.AuthorId;
        });

        AuthenticateAs(collectionAuthorId, "platform-student");

        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var detail = await ReadResultAsync<CollectionDetailDto>(resp);
        Assert.NotNull(detail);
        // Collection author видит свой header без замка.
        Assert.True(detail.IsAccessible);
        Assert.Null(detail.LockReason);

        // Но чужой ENROLLED material — locked, потому что нет grant'ов на курс.
        CollectionItemDto otherItem = detail.Sections.SelectMany(s => s.Items)
            .Single(i => i.ReferenceId == otherAuthorEnrolledId);
        Assert.False(otherItem.IsAccessible,
            "Cross-author ENROLLED material должен быть locked даже если ты автор подборки.");
        // Phase E: cross-author ENROLLED → plan_required (нужен план другого автора).
        Assert.Equal("plan_required", otherItem.LockReason);
    }

    [Fact]
    public async Task Detail_EnrolledCollectionWithMixedItems_Anonymous_PerItemLocks()
    {
        // Detail для анона: header.isAccessible=false (lockReason=anonymous), но
        // внутри секции PUBLIC материал имеет isAccessible=true, ENROLLED — false.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("CL", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        Guid publicId = await CreatePublishedMaterialInDb(AccessType.PUBLIC, ct);
        Guid enrolledId = await CreatePublishedMaterialInDb(AccessType.ENROLLED, ct, courseId);
        await AddItemToCollection(collectionId, publicId, ct);
        await AddItemToCollection(collectionId, enrolledId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        RemoveAuthentication();
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var detail = await ReadResultAsync<CollectionDetailDto>(resp);
        Assert.NotNull(detail);
        Assert.False(detail.IsAccessible);
        Assert.Equal("anonymous", detail.LockReason);

        // Все items находим в первой (и единственной) секции.
        var items = detail.Sections.SelectMany(s => s.Items).ToList();
        CollectionItemDto publicItem = items.Single(i => i.ReferenceId == publicId);
        CollectionItemDto enrolledItem = items.Single(i => i.ReferenceId == enrolledId);

        Assert.True(publicItem.IsAccessible, "PUBLIC item должен быть доступен анону.");
        Assert.Null(publicItem.LockReason);
        Assert.False(enrolledItem.IsAccessible, "ENROLLED item должен быть заблокирован анону.");
        Assert.Equal("anonymous", enrolledItem.LockReason);
    }

    private async Task<Guid> CreateMaterialInDb(CancellationToken ct)
    {
        Guid materialId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId: Guid.NewGuid(),
                title: Title.Create("Mat").Value);
            db.Set<Material>().Add(material);
            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });
        return materialId;
    }

    /// <summary>
    ///     Создаёт <b>опубликованный</b> материал с указанным AccessType. Для тестов
    ///     partial-access подборок: <c>CollectionItemAccessLoader</c> учитывает
    ///     только <c>status='PUBLISHED'</c>, поэтому DRAFT материалы не дают видимости.
    ///
    ///     <para>
    ///     Title генерится уникальным — на materials есть partial-unique индекс
    ///     <c>ix_materials_title WHERE status &lt;&gt; 'DRAFT'</c>, и двукратный create
    ///     с одинаковым "Mat" даёт 23505.
    ///     </para>
    /// </summary>
    private async Task<Guid> CreatePublishedMaterialInDb(
        AccessType accessType,
        CancellationToken ct,
        Guid? courseId = null)
    {
        Guid materialId = Guid.NewGuid();
        string uniqueTitle = $"Mat-{Guid.NewGuid():N}".Substring(0, 32);
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId: Guid.NewGuid(),
                title: Title.Create(uniqueTitle).Value,
                accessType: accessType);
            material.SetContent(MarkdownContent.Create("body").Value);
            UnitResult<Error> publish = material.Publish();
            Assert.True(publish.IsSuccess, publish.IsFailure ? publish.Error.GetMessage() : null);
            db.Set<Material>().Add(material);

            if (courseId is not null)
            {
                db.Set<CourseMaterial>().Add(new CourseMaterial(
                    courseId.Value, material.Id, SortKey.Initial()));
            }
            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });
        return materialId;
    }

    private async Task AddItemToCollection(Guid collectionId, Guid materialId, CancellationToken ct)
    {
        // Section могло уже быть создано прошлым шагом — переиспользуем первое или создаём.
        Guid sectionId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            CollectionSection? sec = await db.Set<CollectionSection>()
                .FirstOrDefaultAsync(s => s.CollectionId == collectionId, ct);
            if (sec is not null) sectionId = sec.Id;
        });

        if (sectionId == Guid.Empty)
        {
            HttpResponseMessage secResp = await AppHttpClient.PostAsJsonAsync(
                $"/collections/{collectionId}/sections",
                new AddSectionRequest(null, null),
                ct);
            secResp.EnsureSuccessStatusCode();
            sectionId = await ReadResultAsync<Guid>(secResp);
        }

        HttpResponseMessage itemResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(materialId),
            ct);
        itemResp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateCourseInDb(CancellationToken ct)
    {
        Guid courseId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId: Guid.NewGuid(),
                title: Title.Create("C").Value,
                description: Description.Create("D").Value,
                slug: CourseSlug.Create("c-" + Guid.NewGuid().ToString("N")[..8]).Value, SortKey.Initial());
            db.Set<Course>().Add(course);
            await db.SaveChangesAsync(ct);
            courseId = course.Id;
        });
        return courseId;
    }

    private async Task PublishCollection(Guid collectionId, CancellationToken ct)
    {
        // Publish требует минимум одного item. Добавляем section + item напрямую в БД
        // чтобы не тянуть материалы через API.
        Guid materialId = await CreateMaterialInDb(ct);
        HttpResponseMessage secResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections",
            new AddSectionRequest(null, null),
            ct);
        secResp.EnsureSuccessStatusCode();
        Guid sectionId = await ReadResultAsync<Guid>(secResp);

        HttpResponseMessage itemResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(materialId),
            ct);
        itemResp.EnsureSuccessStatusCode();

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();
    }

}
