using Common;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts;
using ProgressService.Contracts.Dtos;
using ProgressService.Domain.Bookmarks;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Bookmarks;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class BookmarkRoundTripTests : ProgressServiceTestsBase
{
    public BookmarkRoundTripTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Toggle_RoundTripsCourseAndPersonalListsWithoutLosingPreviousBookmarks()
    {
        Guid userId = Guid.CreateVersion7();
        Guid courseId = Guid.CreateVersion7();
        Guid otherCourseId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        Guid oldIssueId = Guid.CreateVersion7();
        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddResolvedMaterial(courseId, EntityType.Material, materialId,
            "Course", "Lesson", "Module", "Module");
        EducationContentClient.AddResolvedMaterial(otherCourseId, EntityType.Issue, oldIssueId,
            "Other course", "Existing issue", "Project", "Project");
        MaterialBookmark previous = MaterialBookmark.Create(userId, otherCourseId,
            BookmarkEntityReference.Of(EntityType.Issue, oldIssueId).Value).Value;
        await ExecuteInDb(async db =>
        {
            db.MaterialBookmarks.Add(previous);
            await db.SaveChangesAsync();
        });
        MaterialBookmark persistedPrevious = await ExecuteInDb(db => db.MaterialBookmarks.SingleAsync());

        string toggleUrl = $"/progress/courses/{courseId}/bookmarks/Material/{materialId}/";
        HttpResponseMessage add = await AppHttpClient.PutAsync(toggleUrl, content: null);
        add.EnsureSuccessStatusCode();
        HttpResponseMessage courseList = await AppHttpClient.GetAsync($"/progress/bookmarks/?courseId={courseId}");
        courseList.EnsureSuccessStatusCode();
        CursorResponse<BookmarkedMaterialDto> course = await ReadWrappedResultAsync<CursorResponse<BookmarkedMaterialDto>>(courseList);
        Assert.Equal(materialId, Assert.Single(course.Items).Target.Id);
        HttpResponseMessage personalList = await AppHttpClient.GetAsync("/progress/bookmarks/");
        personalList.EnsureSuccessStatusCode();
        CursorResponse<BookmarkedMaterialDto> personal = await ReadWrappedResultAsync<CursorResponse<BookmarkedMaterialDto>>(personalList);
        Assert.Equal(2, personal.Items.Count);
        Assert.Contains(personal.Items, item => item.Target.Id == oldIssueId);
        Assert.Contains(personal.Items, item => item.Target.Id == materialId);

        HttpResponseMessage remove = await AppHttpClient.DeleteAsync(toggleUrl);
        remove.EnsureSuccessStatusCode();
        HttpResponseMessage courseAfter = await AppHttpClient.GetAsync($"/progress/bookmarks/?courseId={courseId}");
        courseAfter.EnsureSuccessStatusCode();
        Assert.Empty((await ReadWrappedResultAsync<CursorResponse<BookmarkedMaterialDto>>(courseAfter)).Items);
        HttpResponseMessage personalAfter = await AppHttpClient.GetAsync("/progress/bookmarks/");
        personalAfter.EnsureSuccessStatusCode();
        Assert.Equal(oldIssueId, Assert.Single((await ReadWrappedResultAsync<CursorResponse<BookmarkedMaterialDto>>(personalAfter)).Items).Target.Id);
        MaterialBookmark remaining = await ExecuteInDb(db => db.MaterialBookmarks.SingleAsync());
        Assert.Equal(previous.Id, remaining.Id);
        Assert.Equal(persistedPrevious.CreatedAt, remaining.CreatedAt);
    }
}
