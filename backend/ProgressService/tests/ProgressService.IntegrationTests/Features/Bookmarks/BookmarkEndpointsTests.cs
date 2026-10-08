using System.Net;
using Common;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts;
using ProgressService.Contracts.Dtos;
using ProgressService.Domain.Bookmarks;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Bookmarks;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class BookmarkEndpointsTests : ProgressServiceTestsBase
{
    public BookmarkEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task PutBookmark_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        Guid courseId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        HttpResponseMessage response = await AppHttpClient.PutAsync(
            BookmarkUrl(courseId, EntityType.Material, lessonId),
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBookmark_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        Guid courseId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            BookmarkUrl(courseId, EntityType.Material, lessonId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutBookmark_CreatesLessonBookmark()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddResolvedMaterial(
            courseId,
            EntityType.Material,
            lessonId,
            "Backend",
            "Lesson 1",
            "Module 1",
            "Module");

        HttpResponseMessage response = await AppHttpClient.PutAsync(
            BookmarkUrl(courseId, EntityType.Material, lessonId),
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MaterialBookmark bookmark = await ExecuteInDb(async db =>
            await db.MaterialBookmarks.SingleAsync());

        Assert.Equal(userId, bookmark.UserId);
        Assert.Equal(courseId, bookmark.CourseId);
        Assert.Equal(EntityType.Material, bookmark.EntityReference.Type);
        Assert.Equal(lessonId, bookmark.EntityReference.Id);
    }

    [Fact]
    public async Task PutBookmark_IsIdempotentForIssue()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddResolvedMaterial(
            courseId,
            EntityType.Issue,
            issueId,
            "Algorithms",
            "Issue 1",
            "Project 1",
            "Project");

        HttpResponseMessage firstResponse = await AppHttpClient.PutAsync(
            BookmarkUrl(courseId, EntityType.Issue, issueId),
            content: null);
        HttpResponseMessage secondResponse = await AppHttpClient.PutAsync(
            BookmarkUrl(courseId, EntityType.Issue, issueId),
            content: null);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        int bookmarksCount = await ExecuteInDb(async db =>
            await db.MaterialBookmarks.CountAsync());

        Assert.Equal(1, bookmarksCount);
    }

    [Fact]
    public async Task PutBookmark_WhenTargetWasNotResolved_ReturnsNotFound()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PutAsync(
            BookmarkUrl(courseId, EntityType.Material, lessonId),
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutBookmark_WhenEducationServiceUnavailable_ReturnsServiceUnavailable()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.SetResolveMaterialsUnavailable(true);

        HttpResponseMessage response = await AppHttpClient.PutAsync(
            BookmarkUrl(courseId, EntityType.Issue, issueId),
            content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBookmark_RemovesExistingBookmark()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(
                    userId,
                    courseId,
                    BookmarkEntityReference.Of(EntityType.Material, lessonId).Value).Value);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            BookmarkUrl(courseId, EntityType.Material, lessonId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int bookmarksCount = await ExecuteInDb(async db =>
            await db.MaterialBookmarks.CountAsync());

        Assert.Equal(0, bookmarksCount);
    }

    [Fact]
    public async Task DeleteBookmark_WhenMissing_ReturnsNoContent()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            BookmarkUrl(courseId, EntityType.Issue, issueId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetBookmarks_ReturnsResolvedItemsAndFiltersStaleRows()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid staleLessonId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddResolvedMaterial(
            courseId,
            EntityType.Material,
            lessonId,
            "Backend",
            "Lesson 1",
            "Module 1",
            "Module");
        EducationContentClient.AddResolvedMaterial(
            courseId,
            EntityType.Issue,
            issueId,
            "Backend",
            "Issue 1",
            "Project 1",
            "Project");

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddRangeAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, lessonId).Value).Value,
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Issue, issueId).Value).Value,
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, staleLessonId).Value).Value);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/bookmarks?limit=10&courseId={courseId}");

        response.EnsureSuccessStatusCode();

        CursorResponse<BookmarkedMaterialDto> result =
            await ReadWrappedResultAsync<CursorResponse<BookmarkedMaterialDto>>(response);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(3, result.TotalCount);
        Assert.DoesNotContain(result.Items, x => x.Target.Id == staleLessonId);
        Assert.Contains(result.Items, x => x.Target.Type == EntityType.Material && x.Target.Id == lessonId);
        Assert.Contains(result.Items, x => x.Target.Type == EntityType.Issue && x.Target.Id == issueId);
    }

    [Fact]
    public async Task GetBookmarks_SupportsEntityTypeFilterAndPagination()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid firstLessonId = Guid.NewGuid();
        Guid secondLessonId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        EducationContentClient.AddResolvedMaterial(courseId, EntityType.Material, firstLessonId, "Backend", "Lesson 1", "Module 1", "Module");
        EducationContentClient.AddResolvedMaterial(courseId, EntityType.Material, secondLessonId, "Backend", "Lesson 2", "Module 1", "Module");
        EducationContentClient.AddResolvedMaterial(courseId, EntityType.Issue, issueId, "Backend", "Issue 1", "Project 1", "Project");

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, firstLessonId).Value).Value);
            await db.SaveChangesAsync();
        });

        await Task.Delay(20);

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, secondLessonId).Value).Value);
            await db.SaveChangesAsync();
        });

        await Task.Delay(20);

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Issue, issueId).Value).Value);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage firstPageResponse = await AppHttpClient.GetAsync(
            $"/progress/bookmarks?limit=1&courseId={courseId}&entityType=Material");
        firstPageResponse.EnsureSuccessStatusCode();

        CursorResponse<BookmarkedMaterialDto> firstPage =
            await ReadWrappedResultAsync<CursorResponse<BookmarkedMaterialDto>>(firstPageResponse);

        Assert.Single(firstPage.Items);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.All(firstPage.Items, item => Assert.Equal(EntityType.Material, item.Target.Type));

        HttpResponseMessage secondPageResponse = await AppHttpClient.GetAsync(
            $"/progress/bookmarks?limit=1&courseId={courseId}&entityType=Material&cursor={firstPage.NextCursor}");
        secondPageResponse.EnsureSuccessStatusCode();

        CursorResponse<BookmarkedMaterialDto> secondPage =
            await ReadWrappedResultAsync<CursorResponse<BookmarkedMaterialDto>>(secondPageResponse);

        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.NotEqual(firstPage.Items[0].Target.Id, secondPage.Items[0].Target.Id);
        // #512 — COUNT(*) считается только на первой странице; cursor-страницы
        // отдают TotalCount=0 (фронт читает totalCount исключительно из pages[0]).
        Assert.Equal(0, secondPage.TotalCount);
    }

    [Fact]
    public async Task GetMyBookmarkIds_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/bookmarks/me/ids");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyBookmarkIds_ReturnsBookmarksFilteredByCourseIds()
    {
        Guid userId = Guid.NewGuid();
        Guid courseAId = Guid.NewGuid();
        Guid courseBId = Guid.NewGuid();
        Guid materialA1 = Guid.NewGuid();
        Guid materialB1 = Guid.NewGuid();
        Guid issueA1 = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseAId, BookmarkEntityReference.Of(EntityType.Material, materialA1).Value).Value);
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseAId, BookmarkEntityReference.Of(EntityType.Issue, issueA1).Value).Value);
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseBId, BookmarkEntityReference.Of(EntityType.Material, materialB1).Value).Value);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/bookmarks/me/ids?courseIds={courseAId}");
        response.EnsureSuccessStatusCode();

        CursorResponse<BookmarkIdDto> result =
            await ReadWrappedResultAsync<CursorResponse<BookmarkIdDto>>(response);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(courseAId, item.CourseId));
        Assert.Contains(result.Items, x => x.Target.Type == EntityType.Material && x.Target.Id == materialA1);
        Assert.Contains(result.Items, x => x.Target.Type == EntityType.Issue && x.Target.Id == issueA1);
        Assert.DoesNotContain(result.Items, x => x.Target.Id == materialB1);
    }

    [Fact]
    public async Task GetMyBookmarkIds_WithoutCourseFilter_ReturnsAllUserBookmarks()
    {
        Guid userId = Guid.NewGuid();
        Guid courseAId = Guid.NewGuid();
        Guid courseBId = Guid.NewGuid();
        Guid materialA1 = Guid.NewGuid();
        Guid materialB1 = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseAId, BookmarkEntityReference.Of(EntityType.Material, materialA1).Value).Value);
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseBId, BookmarkEntityReference.Of(EntityType.Material, materialB1).Value).Value);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/bookmarks/me/ids");
        response.EnsureSuccessStatusCode();

        CursorResponse<BookmarkIdDto> result =
            await ReadWrappedResultAsync<CursorResponse<BookmarkIdDto>>(response);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GetMyBookmarkIds_OnlyReturnsCallerBookmarks()
    {
        Guid callerId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid callerMaterialId = Guid.NewGuid();
        Guid otherMaterialId = Guid.NewGuid();

        AuthenticateAs(callerId, "platform-participant");

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(callerId, courseId, BookmarkEntityReference.Of(EntityType.Material, callerMaterialId).Value).Value);
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(otherUserId, courseId, BookmarkEntityReference.Of(EntityType.Material, otherMaterialId).Value).Value);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/bookmarks/me/ids?courseIds={courseId}");
        response.EnsureSuccessStatusCode();

        CursorResponse<BookmarkIdDto> result =
            await ReadWrappedResultAsync<CursorResponse<BookmarkIdDto>>(response);

        Assert.Single(result.Items);
        Assert.Equal(callerMaterialId, result.Items[0].Target.Id);
    }

    [Fact]
    public async Task GetMyBookmarkIds_SupportsCursorPagination()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, first).Value).Value);
            await db.SaveChangesAsync();
        });

        await Task.Delay(20);

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, second).Value).Value);
            await db.SaveChangesAsync();
        });

        await Task.Delay(20);

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, third).Value).Value);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage firstPageResponse = await AppHttpClient.GetAsync(
            $"/progress/bookmarks/me/ids?courseIds={courseId}&limit=2");
        firstPageResponse.EnsureSuccessStatusCode();

        CursorResponse<BookmarkIdDto> firstPage =
            await ReadWrappedResultAsync<CursorResponse<BookmarkIdDto>>(firstPageResponse);

        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(3, firstPage.TotalCount);

        HttpResponseMessage secondPageResponse = await AppHttpClient.GetAsync(
            $"/progress/bookmarks/me/ids?courseIds={courseId}&limit=2&cursor={firstPage.NextCursor}");
        secondPageResponse.EnsureSuccessStatusCode();

        CursorResponse<BookmarkIdDto> secondPage =
            await ReadWrappedResultAsync<CursorResponse<BookmarkIdDto>>(secondPageResponse);

        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);

        Guid[] returnedIds = firstPage.Items.Concat(secondPage.Items).Select(x => x.Target.Id).ToArray();
        Assert.Equal(3, returnedIds.Distinct().Count());
        Assert.Contains(first, returnedIds);
        Assert.Contains(second, returnedIds);
        Assert.Contains(third, returnedIds);
    }

    [Fact]
    public async Task GetMyBookmarkIds_RejectsLimitExceeding500()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/bookmarks/me/ids?limit=501");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMyBookmarkIds_EmptyResult_ReturnsZeroItemsAndNullCursor()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/bookmarks/me/ids");
        response.EnsureSuccessStatusCode();

        CursorResponse<BookmarkIdDto> result =
            await ReadWrappedResultAsync<CursorResponse<BookmarkIdDto>>(response);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetMyBookmarkIds_LimitOne_PaginatesCorrectly()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, first).Value).Value);
            await db.SaveChangesAsync();
        });

        await Task.Delay(20);

        await ExecuteInDb(async db =>
        {
            await db.MaterialBookmarks.AddAsync(
                MaterialBookmark.Create(userId, courseId, BookmarkEntityReference.Of(EntityType.Material, second).Value).Value);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage firstPageResponse = await AppHttpClient.GetAsync(
            $"/progress/bookmarks/me/ids?courseIds={courseId}&limit=1");
        firstPageResponse.EnsureSuccessStatusCode();

        CursorResponse<BookmarkIdDto> firstPage =
            await ReadWrappedResultAsync<CursorResponse<BookmarkIdDto>>(firstPageResponse);

        Assert.Single(firstPage.Items);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.NotNull(firstPage.NextCursor);
    }

    private static string BookmarkUrl(Guid courseId, EntityType entityType, Guid entityId) =>
        $"/progress/courses/{courseId}/bookmarks/{entityType}/{entityId}";
}
