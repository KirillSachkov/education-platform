using Common;
using ContentAccess;
using System.Net;
using PlatformAuth.Authorization;
using SearchService.Contracts;
using SearchService.Core;
using SearchService.Domain;
using SearchService.IntegrationTests.Infrastructure;

namespace SearchService.IntegrationTests.Features.EducationDocuments.Queries;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetDocumentsTests : SearchServiceTestsBase
{
    public GetDocumentsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Anonymous_user_sees_all_documents_with_lock_on_gated_ones()
    {
        string token = $"anon-{Guid.NewGuid():N}";
        Guid publicCourseId = Guid.NewGuid();
        Guid privateCourseId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} public",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: publicCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} registered",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            courseId: publicCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} enrolled",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.Course(privateCourseId)],
            courseId: privateCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} trial",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.CourseTrial(privateCourseId)],
            courseId: privateCourseId));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50));

        // Search-with-locks: все опубликованные документы в выдаче,
        // недоступные идут с замком + LockReason.
        Assert.Equal(4, result.Hits.Count);

        EducationDocumentDto publicDoc = result.Hits.First(h => h.Document.Title.EndsWith("public", StringComparison.Ordinal)).Document;
        Assert.True(publicDoc.IsAccessible);
        Assert.Null(publicDoc.LockReason);

        foreach (SearchHit<EducationDocumentDto> hit in result.Hits
            .Where(h => !h.Document.Title.EndsWith("public", StringComparison.Ordinal)))
        {
            Assert.False(hit.Document.IsAccessible);
            Assert.Equal(LockReasons.ANONYMOUS, hit.Document.LockReason);
        }
    }

    [Fact]
    public async Task Anonymous_user_does_not_receive_content_highlight_for_locked_material()
    {
        string token = $"locked-body-{Guid.NewGuid():N}";

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "locked body match",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            content: $"секретный фрагмент {token}"));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50));

        SearchHit<EducationDocumentDto> hit = Assert.Single(result.Hits);
        Assert.False(hit.Document.IsAccessible);
        Assert.DoesNotContain(hit.Highlights, h => h.Field == "content");
    }

    [Fact]
    public async Task Authenticated_user_sees_registered_as_accessible_paid_as_locked()
    {
        string token = $"registered-{Guid.NewGuid():N}";
        Guid courseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} public",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: courseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} registered",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            courseId: courseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} trial",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.CourseTrial(courseId)],
            courseId: courseId));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50),
            user => user.Authenticate(userId, "Participant", "participant@test.local", [PlatformRoles.PARTICIPANT]));

        Assert.Equal(3, result.Hits.Count);

        EducationDocumentDto trialDoc = result.Hits.First(h => h.Document.Title.EndsWith("trial", StringComparison.Ordinal)).Document;
        Assert.False(trialDoc.IsAccessible);
        Assert.Equal(LockReasons.TRIAL_REQUIRED, trialDoc.LockReason);

        foreach (SearchHit<EducationDocumentDto> hit in result.Hits
            .Where(h => !h.Document.Title.EndsWith("trial", StringComparison.Ordinal)))
        {
            Assert.True(hit.Document.IsAccessible);
        }
    }

    [Fact]
    public async Task Trial_user_sees_all_with_standard_required_for_enrolled_content()
    {
        string token = $"trial-{Guid.NewGuid():N}";
        Guid entitledCourseId = Guid.NewGuid();
        Guid otherCourseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} my-trial",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.CourseTrial(entitledCourseId)],
            courseId: entitledCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} my-enrolled",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.Course(entitledCourseId)],
            courseId: entitledCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} foreign-trial",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.CourseTrial(otherCourseId)],
            courseId: otherCourseId));

        await SeedTrialCourseAsync(userId, entitledCourseId);

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50),
            user => user.Authenticate(userId, "Student", "student@test.local", [PlatformRoles.PARTICIPANT]));

        Assert.Equal(3, result.Hits.Count);

        EducationDocumentDto myTrial = result.Hits.First(h => h.Document.Title.EndsWith("my-trial", StringComparison.Ordinal)).Document;
        Assert.True(myTrial.IsAccessible);

        EducationDocumentDto myEnrolled = result.Hits.First(h => h.Document.Title.EndsWith("my-enrolled", StringComparison.Ordinal)).Document;
        Assert.False(myEnrolled.IsAccessible);
        Assert.Equal(LockReasons.STANDARD_REQUIRED, myEnrolled.LockReason);

        EducationDocumentDto foreignTrial = result.Hits.First(h => h.Document.Title.EndsWith("foreign-trial", StringComparison.Ordinal)).Document;
        Assert.False(foreignTrial.IsAccessible);
        Assert.Equal(LockReasons.TRIAL_REQUIRED, foreignTrial.LockReason);
    }

    [Fact]
    public async Task Enrolled_user_sees_paid_course_as_accessible_foreign_as_locked()
    {
        string token = $"enrolled-{Guid.NewGuid():N}";
        Guid enrolledCourseId = Guid.NewGuid();
        Guid otherCourseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} my-course",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.Course(enrolledCourseId)],
            courseId: enrolledCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} foreign-course",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.Course(otherCourseId)],
            courseId: otherCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} my-trial",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.CourseTrial(enrolledCourseId)],
            courseId: enrolledCourseId));

        await SeedEnrolledCourseAsync(userId, enrolledCourseId);

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50),
            user => user.Authenticate(userId, "Student", "student@test.local", [PlatformRoles.PARTICIPANT]));

        Assert.Equal(3, result.Hits.Count);

        EducationDocumentDto myCourse = result.Hits.First(h => h.Document.Title.EndsWith("my-course", StringComparison.Ordinal)).Document;
        Assert.True(myCourse.IsAccessible);

        EducationDocumentDto myTrial = result.Hits.First(h => h.Document.Title.EndsWith("my-trial", StringComparison.Ordinal)).Document;
        Assert.True(myTrial.IsAccessible);

        EducationDocumentDto foreignCourse = result.Hits.First(h => h.Document.Title.EndsWith("foreign-course", StringComparison.Ordinal)).Document;
        Assert.False(foreignCourse.IsAccessible);
        Assert.Equal(LockReasons.NOT_ENROLLED, foreignCourse.LockReason);
    }

    [Fact]
    public async Task Course_page_search_should_limit_results_to_requested_course()
    {
        string token = $"course-page-{Guid.NewGuid():N}";
        Guid selectedCourseId = Guid.NewGuid();
        Guid anotherCourseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} selected",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: selectedCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} another",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: anotherCourseId));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50, courseId: selectedCourseId),
            user => user.Authenticate(userId, "Participant", "participant@test.local", [PlatformRoles.PARTICIPANT]));

        Assert.Single(result.Hits);
        Assert.Equal(selectedCourseId, result.Hits[0].Document.CourseId);
    }

    [Fact]
    public async Task Search_should_filter_documents_by_any_selected_tag()
    {
        string token = $"tags-{Guid.NewGuid():N}";
        Guid selectedTagId = Guid.NewGuid();
        Guid anotherTagId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} selected",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [selectedTagId],
            tagTitles: ["selected"]));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} mixed",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [anotherTagId],
            tagTitles: ["another"]));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50, tagIds: [selectedTagId]));

        Assert.Single(result.Hits);
        Assert.Equal($"{token} selected", result.Hits[0].Document.Title);
    }

    [Fact]
    public async Task Search_should_return_documents_when_only_tag_filter_is_provided()
    {
        Guid selectedTagId = Guid.NewGuid();
        Guid anotherTagId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "selected tag document",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [selectedTagId],
            tagTitles: ["selected"]));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "another tag document",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [anotherTagId],
            tagTitles: ["another"]));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(string.Empty, pageSize: 50, tagIds: [selectedTagId]));

        Assert.Single(result.Hits);
        Assert.Equal("selected tag document", result.Hits[0].Document.Title);
    }

    [Fact]
    public async Task Search_should_return_documents_when_only_course_filter_is_provided()
    {
        Guid selectedCourseId = Guid.NewGuid();
        Guid anotherCourseId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "selected course document",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: selectedCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "another course document",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: anotherCourseId));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(string.Empty, pageSize: 50, courseId: selectedCourseId));

        Assert.Single(result.Hits);
        Assert.Equal("selected course document", result.Hits[0].Document.Title);
    }

    [Fact]
    public async Task Search_should_return_course_slug_for_navigation()
    {
        string token = $"course-slug-{Guid.NewGuid():N}";

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} selected",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: Guid.NewGuid(),
            courseTitle: "Course title",
            courseSlug: "course-slug"));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50));

        Assert.Single(result.Hits);
        Assert.Equal("course-slug", result.Hits[0].Document.CourseSlug);
    }

    [Fact]
    public async Task Search_should_accept_maximum_page_size()
    {
        string token = $"max-page-{Guid.NewGuid():N}";

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} public",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: Constants.MAX_PAGE_SIZE));

        Assert.Single(result.Hits);
        Assert.Equal(Constants.MAX_PAGE_SIZE, result.PageSize);
    }

    [Fact]
    public async Task Search_should_fail_when_page_size_exceeds_maximum()
    {
        var result = await ExecuteSearchAsync(
            new SearchRequest($"overflow-{Guid.NewGuid():N}", pageSize: Constants.MAX_PAGE_SIZE + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("Размер страницы должен быть от 1 до 100", result.Error.Messages[0].Message);
    }

    [Fact]
    public async Task Search_should_reject_page_beyond_typesense_result_window()
    {
        var result = await ExecuteSearchAsync(
            new SearchRequest("deep-page", page: 101, pageSize: 100));

        Assert.True(result.IsFailure);
        Assert.Equal("search.query.result_window.exceeded", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Search_endpoint_should_apply_default_paging_when_omitted()
    {
        string token = $"default-paging-{Guid.CreateVersion7():N}";
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.CreateVersion7(),
            token,
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/search?search={token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"page\":1", payload, StringComparison.Ordinal);
        Assert.Contains("\"pageSize\":20", payload, StringComparison.Ordinal);
        Assert.Contains(token, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_should_reject_oversized_query_and_filter_arrays()
    {
        var oversizedQuery = await ExecuteSearchAsync(
            new SearchRequest(new string('x', Constants.MAX_QUERY_LENGTH + 1)));
        var oversizedTags = await ExecuteSearchAsync(
            new SearchRequest(
                string.Empty,
                tagIds: Enumerable.Range(0, Constants.MAX_FILTER_VALUES + 1)
                    .Select(_ => Guid.CreateVersion7())
                    .ToArray()));

        Assert.True(oversizedQuery.IsFailure);
        Assert.Equal("search.query.too_long", oversizedQuery.Error.Messages[0].Code);
        Assert.True(oversizedTags.IsFailure);
        Assert.Equal("search.query.too_many_tags", oversizedTags.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Search_should_reject_unsupported_entity_type()
    {
        var result = await ExecuteSearchAsync(
            new SearchRequest(string.Empty, entityTypes: [EntityType.Quiz]));

        Assert.True(result.IsFailure);
        Assert.Equal("search.query.entity_type.unsupported", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Search_should_reject_invalid_browse_cursor_and_access_filter()
    {
        var invalidCursor = await ExecuteSearchAsync(
            new SearchRequest(string.Empty, courseId: Guid.CreateVersion7(), cursor: "not-a-cursor"));
        var invalidAccessFilter = await ExecuteSearchAsync(
            new SearchRequest("query", accessFilter: "all"));

        Assert.True(invalidCursor.IsFailure);
        Assert.Equal("search.query.cursor.invalid", invalidCursor.Error.Messages[0].Code);
        Assert.True(invalidAccessFilter.IsFailure);
        Assert.Equal("search.query.access_filter.invalid", invalidAccessFilter.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Browse_cursor_should_page_without_returning_a_dangling_final_cursor()
    {
        Guid courseId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.CreateVersion7(),
            "newer browse document",
            now,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: courseId));
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.CreateVersion7(),
            "older browse document",
            now.AddMinutes(-1),
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: courseId));

        SearchResponse<EducationDocumentDto> first = await SearchAsync(
            new SearchRequest(string.Empty, pageSize: 1, courseId: courseId));

        Assert.Equal("newer browse document", Assert.Single(first.Hits).Document.Title);
        Assert.NotNull(first.NextCursor);

        SearchResponse<EducationDocumentDto> second = await SearchAsync(
            new SearchRequest(
                string.Empty,
                page: 99, // cursor contract ignores an independently supplied page
                pageSize: 1,
                courseId: courseId,
                cursor: first.NextCursor));

        Assert.Equal("older browse document", Assert.Single(second.Hits).Document.Title);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Search_should_fail_when_query_contains_only_whitespace()
    {
        var result = await ExecuteSearchAsync(
            new SearchRequest("   ", pageSize: Constants.MAX_PAGE_SIZE));

        Assert.True(result.IsFailure);
        Assert.Equal("Поисковый запрос или хотя бы один фильтр обязателен", result.Error.Messages[0].Message);
    }

    [Fact]
    public async Task Search_endpoint_should_fail_when_query_is_wildcard_only()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/search?search=*&page=1&pageSize={Constants.MAX_PAGE_SIZE}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();

        Assert.Contains("search.query.wildcard.invalid", payload, StringComparison.Ordinal);
        Assert.Contains("Wildcard-only query is not allowed", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_endpoint_should_return_results_when_query_is_missing_but_tag_filter_is_provided()
    {
        Guid selectedTagId = Guid.NewGuid();
        Guid anotherTagId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "selected tag endpoint document",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [selectedTagId],
            tagTitles: ["selected"]));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "another tag endpoint document",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            tagIds: [anotherTagId],
            tagTitles: ["another"]));

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/search?tagIds={selectedTagId}&page=1&pageSize={Constants.MAX_PAGE_SIZE}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();

        Assert.Contains("selected tag endpoint document", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("another tag endpoint document", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_endpoint_should_return_results_when_query_is_missing_but_course_filter_is_provided()
    {
        Guid selectedCourseId = Guid.NewGuid();
        Guid anotherCourseId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "selected course endpoint document",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: selectedCourseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            "another course endpoint document",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            courseId: anotherCourseId));

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/search?courseId={selectedCourseId}&page=1&pageSize={Constants.MAX_PAGE_SIZE}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();

        Assert.Contains("selected course endpoint document", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("another course endpoint document", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_should_filter_documents_by_author_id()
    {
        // ARCH-1: scope поиска по пространству автора. Проверяем что authorId фильтр
        // возвращает только документы с совпавшим author_id и отсекает остальные.
        string token = $"author-{Guid.NewGuid():N}";
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} by-author-a",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            authorId: authorA));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} by-author-b",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            authorId: authorB));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} no-author",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50, authorId: authorA));

        Assert.Single(result.Hits);
        Assert.Equal($"{token} by-author-a", result.Hits[0].Document.Title);
    }

    [Fact]
    public async Task Search_without_author_id_returns_all_authors_documents()
    {
        // Backwards-compat: без authorId фильтра видны документы всех авторов
        // (включая legacy без author_id в индексе — поле optional).
        string token = $"all-authors-{Guid.NewGuid():N}";
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} by-a",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            authorId: authorA));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} by-b",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            authorId: authorB));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} legacy",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC]));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50));

        Assert.Equal(3, result.Hits.Count);
    }

    [Fact]
    public async Task FreeFilter_returns_docs_marked_free_by_author_regardless_of_caller_grants()
    {
        // Регрессия на issue #255: фильтр `accessFilter=free` показывал не «то, что автор
        // пометил бесплатным», а «то, что доступно вызывающему». Анон видел только PUBLIC,
        // залогиненный без планов — PUBLIC+REGISTERED, и FREE-материалы автора скрывались.
        // Issue #358: AccessType.FREE удалён; бесплатный = REGISTERED.
        // Ожидание: для каждого вызывающего видны все материалы с
        // AccessType ∈ {PUBLIC, REGISTERED} (тегги access:public + authenticated).
        string token = $"freefilter-{Guid.NewGuid():N}";
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} public",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            authorId: authorId,
            courseId: courseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} registered",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            authorId: authorId,
            courseId: courseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} enrolled",
            DateTime.UtcNow,
            requiredAccessTags:
            [
                GrantTags.PlanAll(),
                GrantTags.PlanCourse(courseId),
            ],
            authorId: authorId,
            courseId: courseId));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50, authorId: authorId, accessFilter: "free"));

        Assert.Equal(2, result.Hits.Count);
        Assert.DoesNotContain(
            result.Hits,
            h => h.Document.Title.EndsWith("enrolled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublicFilter_returns_only_materials_available_without_authentication()
    {
        string token = $"publicfilter-{Guid.NewGuid():N}";
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} public",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            authorId: authorId,
            courseId: courseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} registered",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            authorId: authorId,
            courseId: courseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} enrolled",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PlanCourse(courseId)],
            authorId: authorId,
            courseId: courseId));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 1, authorId: authorId, accessFilter: "public"));

        EducationDocumentDto hit = Assert.Single(result.Hits).Document;
        Assert.EndsWith("public", hit.Title, StringComparison.Ordinal);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task FreeFilter_for_admin_still_excludes_enrolled_docs()
    {
        // Регрессия на issue #279: admin-shortcut в `BuildAccessTypeFilter` бэйпасил
        // фильтр и возвращал всю выдачу (включая ENROLLED) — баг. Это UI-фильтр выдачи,
        // не access-check; admin обязан получать ту же фильтрованную выборку, что и
        // другие роли. Lock-shortcut админа живёт в `LockReasonResolver` (он и так
        // видит замки нет), отдельный shortcut здесь ломает semantics.
        // Issue #358: AccessType.FREE удалён; бесплатный = REGISTERED.
        string token = $"freefilter-admin-{Guid.NewGuid():N}";
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid adminId = Guid.NewGuid();

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} public",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            authorId: authorId,
            courseId: courseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} registered",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            authorId: authorId,
            courseId: courseId));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} enrolled",
            DateTime.UtcNow,
            requiredAccessTags:
            [
                GrantTags.PlanAll(),
                GrantTags.PlanCourse(courseId),
            ],
            authorId: authorId,
            courseId: courseId));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50, authorId: authorId, accessFilter: "free"),
            user => user.Authenticate(adminId, "Admin", "admin@test.local", [PlatformRoles.ADMIN]));

        Assert.Equal(2, result.Hits.Count);
        Assert.DoesNotContain(
            result.Hits,
            h => h.Document.Title.EndsWith("enrolled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_search_includes_soft_deleted_documents_for_moderation()
    {
        string token = $"admin-deleted-{Guid.CreateVersion7():N}";
        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.CreateVersion7(),
            token,
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            isDeleted: true));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token),
            user => user.Authenticate(
                Guid.CreateVersion7(),
                "Admin",
                "admin@test.local",
                [PlatformRoles.ADMIN]));

        EducationDocumentDto document = Assert.Single(result.Hits).Document;
        Assert.True(document.IsAccessible);
        Assert.Null(document.LockReason);
    }

    [Fact]
    public async Task Search_should_filter_documents_by_material_kind()
    {
        string token = $"kind-{Guid.NewGuid():N}";

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} article",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            materialKind: "ARTICLE"));

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} video",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            materialKind: "VIDEO"));

        SearchResponse<EducationDocumentDto> result = await SearchAsync(
            new SearchRequest(token, pageSize: 50, materialKind: "VIDEO"));

        Assert.Single(result.Hits);
        Assert.Equal($"{token} video", result.Hits[0].Document.Title);
    }

    [Fact]
    public async Task Search_should_reject_material_kind_outside_allowlist()
    {
        // Без allowlist-валидации material_kind подставлялся в Typesense filter_by как
        // free-string, и значение с backtick + `||` могло OR-нуть `is_deleted:=true`,
        // раскрыв soft-deleted/draft-документы (filter injection).
        var result = await ExecuteSearchAsync(
            new SearchRequest($"inject-{Guid.NewGuid():N}", pageSize: 50, materialKind: "VIDEO`||is_deleted:=`true"));

        Assert.True(result.IsFailure);
        Assert.Equal("search.query.material_kind.invalid", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Search_injected_material_kind_does_not_leak_soft_deleted_documents()
    {
        // Сквозная регрессия: даже если бы валидатор сломался, проверяем что
        // soft-deleted документ не вытекает. Здесь — что инъекция отклоняется ДО
        // того, как дойдёт до Typesense (валидатор → Failure, до построения фильтра).
        string token = $"inject-leak-{Guid.NewGuid():N}";

        await IndexDocumentAsync(EducationDocument.CreateMaterial(
            Guid.NewGuid(),
            $"{token} hidden-draft",
            DateTime.UtcNow,
            requiredAccessTags: [GrantTags.PUBLIC],
            isDeleted: true,
            materialKind: "ARTICLE"));

        var result = await ExecuteSearchAsync(
            new SearchRequest(token, pageSize: 50, materialKind: $"ARTICLE`||is_deleted:=`true"));

        Assert.True(result.IsFailure);
        Assert.Equal("search.query.material_kind.invalid", result.Error.Messages[0].Code);
    }
}
