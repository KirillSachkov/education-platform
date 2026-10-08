using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts;
using TrainerService.Contracts.Bookmarks;
using TrainerService.Contracts.Topics;
using TrainerService.Domain.Bookmarks;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Bookmarks;

public sealed class BookmarkTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Create_list_remove_bookmark_round_trip()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicWithBankAsync();
        AuthenticateAs("platform-participant");
        Guid questionId = q.SingleQuestionId;

        // Create → 200 with bookmark.
        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/bookmarks",
            new CreateBookmarkRequest(topicId, questionId));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        BookmarkDto created = await ReadResultAsync<BookmarkDto>(createResponse);
        Assert.Equal(topicId, created.TopicId);
        Assert.Equal(questionId, created.QuestionId);

        // List → contains it.
        IReadOnlyList<BookmarkDto> afterCreate = await ListBookmarksAsync();
        Assert.Single(afterCreate, b => b.TopicId == topicId && b.QuestionId == questionId);

        // Remove → 200.
        HttpResponseMessage removeResponse =
            await Client.DeleteAsync($"/trainer/bookmarks/{questionId}");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        // List → empty.
        IReadOnlyList<BookmarkDto> afterRemove = await ListBookmarksAsync();
        Assert.DoesNotContain(afterRemove, b => b.QuestionId == questionId);
    }

    [Fact]
    public async Task Duplicate_bookmark_returns_409()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicWithBankAsync();
        AuthenticateAs("platform-participant");
        Guid questionId = q.SingleQuestionId;

        HttpResponseMessage first = await Client.PostAsJsonAsync(
            "/trainer/bookmarks",
            new CreateBookmarkRequest(topicId, questionId));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        HttpResponseMessage second = await Client.PostAsJsonAsync(
            "/trainer/bookmarks",
            new CreateBookmarkRequest(topicId, questionId));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("trainer.bookmark.already.exists", await ReadErrorCodeAsync(second));
    }

    [Fact]
    public async Task Bookmarks_are_scoped_per_user()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicWithBankAsync();
        Guid questionId = q.SingleQuestionId;

        // User A bookmarks.
        Guid userA = Guid.NewGuid();
        AuthenticateAs("platform-participant", userA);
        await Client.PostAsJsonAsync("/trainer/bookmarks", new CreateBookmarkRequest(topicId, questionId));

        // User B sees no bookmarks.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        IReadOnlyList<BookmarkDto> userBList = await ListBookmarksAsync();
        Assert.Empty(userBList);
    }

    [Fact]
    public async Task List_enriches_bookmarks_with_question_content()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicWithBankAsync();
        AuthenticateAs("platform-participant");
        Guid questionId = q.SingleQuestionId;

        await Client.PostAsJsonAsync("/trainer/bookmarks", new CreateBookmarkRequest(topicId, questionId));

        IReadOnlyList<BookmarkDto> list = await ListBookmarksAsync();
        BookmarkDto bookmark = Assert.Single(list);

        // Stem + difficulty come from the local question bank; topic title from the local topic.
        Assert.Equal(TrainerQuestionFixtures.SingleStem, bookmark.Stem);
        Assert.Equal("JUNIOR", bookmark.Difficulty);
        Assert.Equal("Тема закладок", bookmark.TopicTitle);

        // No grading key leaked through the enriched list (preview only).
        string raw = await ReadRawAsync(await Client.GetAsync("/trainer/bookmarks"));
        Assert.DoesNotContain(q.SingleCorrectOption.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctOptionIds", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_returns_bookmark_with_null_stem_when_question_deleted()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicWithBankAsync();
        AuthenticateAs("platform-participant");

        // Bookmark a question, then hard-delete it from the bank — the enriched list degrades to
        // a null stem rather than crashing (the bookmark row survives).
        Guid questionId = q.SingleQuestionId;
        await Client.PostAsJsonAsync("/trainer/bookmarks", new CreateBookmarkRequest(topicId, questionId));

        AuthenticateAsAdmin();
        HttpResponseMessage delete = await Client.DeleteAsync($"/trainer/questions/{questionId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        AuthenticateAs("platform-participant");
        IReadOnlyList<BookmarkDto> list = await ListBookmarksAsync();
        BookmarkDto bookmark = Assert.Single(list);

        // Bookmark still returned (does not crash); stem/difficulty null, topic title still resolves.
        Assert.Equal(questionId, bookmark.QuestionId);
        Assert.Null(bookmark.Stem);
        Assert.Null(bookmark.Difficulty);
        Assert.Equal("Тема закладок", bookmark.TopicTitle);
    }

    [Fact]
    public async Task Create_bookmark_rejects_question_outside_topic_bank()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicWithBankAsync();
        AuthenticateAs("platform-participant");

        // A question id that belongs to no bank of this topic → not found.
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/bookmarks",
            new CreateBookmarkRequest(topicId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("trainer.question.not.found", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task List_paginates_with_keyset_cursor_newest_first()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicWithBankAsync();
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        // 5 bookmarks; V7 ids are monotonic + same-ish CreatedAt → newest-first == reverse of insertion.
        IReadOnlyList<BookmarkedQuestion> seeded = await SeedBookmarksAsync(userId, topicId, 5);
        List<Guid> expectedOrder = seeded.Select(b => b.Id).Reverse().ToList();

        // Page 1 (limit 2) → first two newest + a non-null cursor.
        CursorResponse<BookmarkDto> page1 = await ListBookmarksPageAsync(limit: 2);
        Assert.Equal(2, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);
        Assert.Equal(expectedOrder[0], page1.Items[0].Id);
        Assert.Equal(expectedOrder[1], page1.Items[1].Id);

        // Page 2 → next two, still has more.
        CursorResponse<BookmarkDto> page2 = await ListBookmarksPageAsync(page1.NextCursor, limit: 2);
        Assert.Equal(2, page2.Items.Count);
        Assert.NotNull(page2.NextCursor);
        Assert.Equal(expectedOrder[2], page2.Items[0].Id);
        Assert.Equal(expectedOrder[3], page2.Items[1].Id);

        // Page 3 → last one, nextCursor null (exhausted).
        CursorResponse<BookmarkDto> page3 = await ListBookmarksPageAsync(page2.NextCursor, limit: 2);
        Assert.Single(page3.Items);
        Assert.Null(page3.NextCursor);
        Assert.Equal(expectedOrder[4], page3.Items[0].Id);

        // No overlap / no gaps across the walked pages.
        List<Guid> walked = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.Id).ToList();
        Assert.Equal(expectedOrder, walked);
    }

    [Fact]
    public async Task List_clamps_limit_to_max_50()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicWithBankAsync();
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        await SeedBookmarksAsync(userId, topicId, 55);

        // Over-max limit is clamped to 50 → exactly 50 returned, more remain.
        CursorResponse<BookmarkDto> page = await ListBookmarksPageAsync(limit: 1000);
        Assert.Equal(50, page.Items.Count);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task List_treats_garbage_cursor_as_first_page()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicWithBankAsync();
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        IReadOnlyList<BookmarkedQuestion> seeded = await SeedBookmarksAsync(userId, topicId, 3);
        Guid newest = seeded.Select(b => b.Id).Last();

        // Invalid cursor must not 500 — it's treated as "start from beginning".
        CursorResponse<BookmarkDto> page = await ListBookmarksPageAsync("not-a-valid-cursor", limit: 2);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(newest, page.Items[0].Id);
        Assert.NotNull(page.NextCursor);
    }

    private async Task<IReadOnlyList<BookmarkDto>> ListBookmarksAsync()
    {
        CursorResponse<BookmarkDto> page = await ListBookmarksPageAsync();
        return page.Items;
    }

    private async Task<CursorResponse<BookmarkDto>> ListBookmarksPageAsync(string? cursor = null, int? limit = null)
    {
        string url = "/trainer/bookmarks";
        var qs = new List<string>();
        if (cursor is not null) qs.Add($"cursor={Uri.EscapeDataString(cursor)}");
        if (limit is { } l) qs.Add($"limit={l}");
        if (qs.Count > 0) url += "?" + string.Join('&', qs);

        HttpResponseMessage response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<CursorResponse<BookmarkDto>>(response);
    }

    /// <summary>
    ///     Seeds <paramref name="count"/> bookmarks for the given user directly through the DbContext
    ///     (each gets a fresh V7 id + UtcNow CreatedAt). Returns them in insertion order (oldest-first).
    ///     Distinct question ids keep the (UserId, QuestionId) unique key happy.
    /// </summary>
    private async Task<IReadOnlyList<BookmarkedQuestion>> SeedBookmarksAsync(Guid userId, Guid topicId, int count) =>
        await ExecuteInDbAsync(async db =>
        {
            var seeded = new List<BookmarkedQuestion>(count);
            for (int i = 0; i < count; i++)
            {
                var bookmark = BookmarkedQuestion.Create(userId, topicId, Guid.NewGuid());
                db.BookmarkedQuestions.Add(bookmark);
                seeded.Add(bookmark);
            }

            await db.SaveChangesAsync();
            return (IReadOnlyList<BookmarkedQuestion>)seeded;
        });

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicWithBankAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createTopic = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "bookmark-topic", "Тема закладок", "Runtime", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, createTopic.StatusCode);
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createTopic)).TopicId;

        HttpResponseMessage addBank = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Assert.Equal(HttpStatusCode.OK, addBank.StatusCode);
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(addBank)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        HttpResponseMessage publish = await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        return (topicId, questions);
    }
}
