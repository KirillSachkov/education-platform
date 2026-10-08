using System.Net;
using Microsoft.EntityFrameworkCore;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace ProgressService.IntegrationTests.Features.Reviews;

[Collection(nameof(IntegrationTestsFixture))]
public class ReviewIssueListEndpointsTests : ProgressServiceTestsBase
{
    public ReviewIssueListEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetPendingReviewIssues_ReturnsOnlyPendingAndInReview_WithPaginationAndSorting()
    {
        Guid firstCourseId = Guid.NewGuid();
        Guid secondCourseId = Guid.NewGuid();
        Guid firstProjectId = Guid.NewGuid();
        Guid secondProjectId = Guid.NewGuid();
        Guid thirdProjectId = Guid.NewGuid();
        Guid firstIssueId = Guid.NewGuid();
        Guid secondIssueId = Guid.NewGuid();
        Guid thirdIssueId = Guid.NewGuid();
        Guid approvedIssueId = Guid.NewGuid();
        Guid requestedChangesIssueId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();

        SeededSubmission pendingOldest = await SeedSubmissionAsync(
            Guid.NewGuid(), firstCourseId, firstProjectId, firstIssueId, IssueSubmissionReviewStatus.PENDING);
        await Task.Delay(10);
        SeededSubmission inReviewMiddle = await SeedSubmissionAsync(
            Guid.NewGuid(), firstCourseId, secondProjectId, secondIssueId, IssueSubmissionReviewStatus.IN_REVIEW, reviewerId);
        await Task.Delay(10);
        SeededSubmission pendingNewest = await SeedSubmissionAsync(
            Guid.NewGuid(), secondCourseId, thirdProjectId, thirdIssueId, IssueSubmissionReviewStatus.PENDING);
        await Task.Delay(10);
        await SeedSubmissionAsync(
            Guid.NewGuid(), firstCourseId, Guid.NewGuid(), approvedIssueId, IssueSubmissionReviewStatus.APPROVED, reviewerId);
        await Task.Delay(10);
        await SeedSubmissionAsync(
            Guid.NewGuid(), secondCourseId, Guid.NewGuid(), requestedChangesIssueId,
            IssueSubmissionReviewStatus.CHANGES_REQUESTED, reviewerId);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage response =
            await AppHttpClient.GetAsync("/progress/reviews/issues/pending?page=1&pageSize=2");

        response.EnsureSuccessStatusCode();

        ReviewIssuesPagedResponse result = await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(response);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(
            [pendingNewest.SubmissionId, inReviewMiddle.SubmissionId],
            result.Items.Select(x => x.SubmissionId).ToArray());
        Assert.All(result.Items, item => Assert.True(
            new[] { "PENDING", "IN_REVIEW" }.Contains(item.ReviewStatus)));
        Assert.DoesNotContain(result.Items, item => item.SubmissionId == pendingOldest.SubmissionId);
    }

    [Fact]
    public async Task GetReviewedReviewIssues_ReturnsOnlyApprovedAndRequestedChanges_SortedByReviewedAtDesc()
    {
        Guid courseId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();

        SeededSubmission approvedOlder = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.APPROVED, reviewerId);
        await Task.Delay(10);
        SeededSubmission changesRequestedNewest = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.CHANGES_REQUESTED, reviewerId);
        await Task.Delay(10);
        await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage response =
            await AppHttpClient.GetAsync("/progress/reviews/issues/reviewed?page=1&pageSize=10");

        response.EnsureSuccessStatusCode();

        ReviewIssuesPagedResponse result = await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(response);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            [changesRequestedNewest.SubmissionId, approvedOlder.SubmissionId],
            result.Items.Select(x => x.SubmissionId).ToArray());
        Assert.All(result.Items, item => Assert.True(
            new[] { "APPROVED", "CHANGES_REQUESTED" }.Contains(item.ReviewStatus)));
    }

    [Fact]
    public async Task GetPendingReviewIssues_AfterStartReview_ReturnsReviewerId()
    {
        Guid courseId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();

        SeededSubmission pendingSubmission = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);

        AuthenticateAs(reviewerId, "platform-moderator");

        HttpResponseMessage startReviewResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{pendingSubmission.SubmissionId}/start-review");

        Assert.Equal(HttpStatusCode.OK, startReviewResponse.StatusCode);

        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        listResponse.EnsureSuccessStatusCode();

        ReviewIssuesPagedResponse result = await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(listResponse);
        var item = Assert.Single(result.Items);

        Assert.Equal("IN_REVIEW", item.ReviewStatus);
        Assert.Equal(reviewerId, item.ReviewerId);
        Assert.NotNull(item.ReviewStartedAt);
    }

    [Fact]
    public async Task Approve_MovesSubmissionFromPendingToReviewed()
    {
        Guid courseId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();

        SeededSubmission pendingSubmission = await SeedSubmissionAsync(
            Guid.NewGuid(),
            courseId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            IssueSubmissionReviewStatus.PENDING,
            includeProjectProgress: true);

        AuthenticateAs(reviewerId, "platform-moderator");

        HttpResponseMessage startReviewResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{pendingSubmission.SubmissionId}/start-review");
        HttpResponseMessage approveResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{pendingSubmission.SubmissionId}/approve",
            new ApproveIssueRequest("Looks good"));

        Assert.Equal(HttpStatusCode.OK, startReviewResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        HttpResponseMessage pendingListResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        HttpResponseMessage reviewedListResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/reviewed?page=1&pageSize=10&courseId={courseId}");

        pendingListResponse.EnsureSuccessStatusCode();
        reviewedListResponse.EnsureSuccessStatusCode();

        ReviewIssuesPagedResponse pendingList =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(pendingListResponse);
        ReviewIssuesPagedResponse reviewedList =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(reviewedListResponse);

        Assert.Empty(pendingList.Items);
        var reviewedItem = Assert.Single(reviewedList.Items);
        Assert.Equal(pendingSubmission.SubmissionId, reviewedItem.SubmissionId);
        Assert.Equal("APPROVED", reviewedItem.ReviewStatus);
    }

    [Fact]
    public async Task RequestChanges_MovesSubmissionFromPendingToReviewed()
    {
        Guid courseId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();

        SeededSubmission pendingSubmission = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);

        AuthenticateAs(reviewerId, "platform-moderator");

        HttpResponseMessage startReviewResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{pendingSubmission.SubmissionId}/start-review");
        HttpResponseMessage requestChangesResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{pendingSubmission.SubmissionId}/request-changes",
            new RequestIssueChangesRequest("Please rework it"));

        Assert.Equal(HttpStatusCode.OK, startReviewResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, requestChangesResponse.StatusCode);

        HttpResponseMessage pendingListResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        HttpResponseMessage reviewedListResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/reviewed?page=1&pageSize=10&courseId={courseId}");

        pendingListResponse.EnsureSuccessStatusCode();
        reviewedListResponse.EnsureSuccessStatusCode();

        ReviewIssuesPagedResponse pendingList =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(pendingListResponse);
        ReviewIssuesPagedResponse reviewedList =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(reviewedListResponse);

        Assert.Empty(pendingList.Items);
        var reviewedItem = Assert.Single(reviewedList.Items);
        Assert.Equal(pendingSubmission.SubmissionId, reviewedItem.SubmissionId);
        Assert.Equal("CHANGES_REQUESTED", reviewedItem.ReviewStatus);
        Assert.Equal("Please rework it", reviewedItem.Feedback);
    }

    [Fact]
    public async Task GetPendingReviewIssues_CourseFilter_ReturnsOnlyMatchingCourse()
    {
        Guid includedCourseId = Guid.NewGuid();
        Guid excludedCourseId = Guid.NewGuid();

        SeededSubmission included = await SeedSubmissionAsync(
            Guid.NewGuid(), includedCourseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);
        await SeedSubmissionAsync(
            Guid.NewGuid(), excludedCourseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={includedCourseId}");

        response.EnsureSuccessStatusCode();

        ReviewIssuesPagedResponse result = await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(response);

        var item = Assert.Single(result.Items);
        Assert.Equal(included.SubmissionId, item.SubmissionId);
        Assert.Equal(includedCourseId, item.CourseId);
    }

    [Fact]
    public async Task ReviewLists_SubmissionFilter_ReturnsCurrentGroupRepresentativeAcrossTabsAsync()
    {
        Guid courseId = Guid.NewGuid();
        SeededSubmission pending = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);
        SeededSubmission inReview = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.PENDING, gateForAiReview: true);
        SeededSubmission reviewed = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.APPROVED, Guid.NewGuid());

        // Unrelated newer rows prove that pageSize=1 does not hide the deep-link target.
        await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);
        await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.PENDING, gateForAiReview: true);
        await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.APPROVED, Guid.NewGuid());

        AuthenticateAs(Guid.NewGuid(), PlatformRoles.MODERATOR);

        (string Tab, Guid SubmissionId)[] cases =
        [
            ("pending", pending.SubmissionId),
            ("in-review", inReview.SubmissionId),
            ("reviewed", reviewed.SubmissionId),
        ];

        foreach ((string tab, Guid submissionId) in cases)
        {
            HttpResponseMessage response = await AppHttpClient.GetAsync(
                $"/progress/reviews/issues/{tab}?page=1&pageSize=1&submissionId={submissionId}");
            response.EnsureSuccessStatusCode();

            ReviewIssuesPagedResponse result =
                await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(response);
            var item = Assert.Single(result.Items);
            Assert.Equal(1, result.TotalCount);
            Assert.Equal(submissionId, item.SubmissionId);
        }

        IReadOnlyList<Guid> attempts = await SeedAttemptGroupAsync(
            courseId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [IssueSubmissionReviewStatus.PENDING, IssueSubmissionReviewStatus.APPROVED]);

        HttpResponseMessage staleLinkResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/reviewed?page=1&pageSize=1&submissionId={attempts[0]}");
        staleLinkResponse.EnsureSuccessStatusCode();

        ReviewIssuesPagedResponse staleLinkResult =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(staleLinkResponse);
        var currentRepresentative = Assert.Single(staleLinkResult.Items);
        Assert.Equal(attempts[1], currentRepresentative.SubmissionId);
    }

    [Fact]
    public async Task GetPendingReviewIssues_WithoutReviewPermission_ReturnsForbidden()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response =
            await AppHttpClient.GetAsync("/progress/reviews/issues/pending?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPendingReviewIssues_WithCursor_PaginatesThroughAllItems()
    {
        Guid courseId = Guid.NewGuid();

        // Seed 3 PENDING submissions so paging with size=2 requires two pages.
        SeededSubmission oldest = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);
        await Task.Delay(10);
        SeededSubmission middle = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);
        await Task.Delay(10);
        SeededSubmission newest = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        // First page — no cursor param (page=1, pageSize=2). Since `hasMore` is true (3 rows,
        // pageSize=2), the handler emits a nextCursor even in legacy offset-mode so the UI can
        // switch to cursor mode on page 2 without needing a second request.
        HttpResponseMessage firstResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=2&courseId={courseId}");
        firstResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse firstPage =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(firstResponse);

        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(
            [newest.SubmissionId, middle.SubmissionId],
            firstPage.Items.Select(x => x.SubmissionId).ToArray());

        // Second page — pass the cursor returned from page 1. `page` is ignored in cursor mode.
        // The keyset `(submitted_at, id) < (cursor.SubmittedAt, cursor.Id)` returns strictly
        // older rows, so we expect only `oldest` with no overlap with page 1.
        HttpResponseMessage secondResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=2&courseId={courseId}&cursor={firstPage.NextCursor}");
        secondResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse secondPage =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(secondResponse);

        Assert.Single(secondPage.Items);
        Assert.Equal(oldest.SubmissionId, secondPage.Items[0].SubmissionId);
        Assert.Null(secondPage.NextCursor);
        Assert.DoesNotContain(secondPage.Items, item => item.SubmissionId == newest.SubmissionId);
        Assert.DoesNotContain(secondPage.Items, item => item.SubmissionId == middle.SubmissionId);
    }

    [Fact]
    public async Task GetReviewedReviewIssues_WithCursor_PaginatesThroughAllItems()
    {
        Guid courseId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();

        // Seed 3 reviewed submissions (APPROVED/CHANGES_REQUESTED).
        SeededSubmission oldest = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.APPROVED, reviewerId);
        await Task.Delay(10);
        SeededSubmission middle = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.CHANGES_REQUESTED, reviewerId);
        await Task.Delay(10);
        SeededSubmission newest = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.APPROVED, reviewerId);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage firstResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/reviewed?page=1&pageSize=2&courseId={courseId}");
        firstResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse firstPage =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(firstResponse);

        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(
            [newest.SubmissionId, middle.SubmissionId],
            firstPage.Items.Select(x => x.SubmissionId).ToArray());

        HttpResponseMessage secondResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/reviewed?page=1&pageSize=2&courseId={courseId}&cursor={firstPage.NextCursor}");
        secondResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse secondPage =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(secondResponse);

        Assert.Single(secondPage.Items);
        Assert.Equal(oldest.SubmissionId, secondPage.Items[0].SubmissionId);
        Assert.Null(secondPage.NextCursor);
        Assert.DoesNotContain(secondPage.Items, item => item.SubmissionId == newest.SubmissionId);
        Assert.DoesNotContain(secondPage.Items, item => item.SubmissionId == middle.SubmissionId);
    }

    [Fact]
    public async Task GetInReviewIssues_ReturnsOnlyGatedPending_AndExcludesThemFromPendingTab()
    {
        Guid courseId = Guid.NewGuid();

        // Gated PENDING — AI is reviewing, hidden from the human queue. Should appear in «В проверке».
        SeededSubmission gated = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.PENDING, gateForAiReview: true);
        // Plain PENDING — ready for human review. Belongs to «Ожидают проверки», NOT «В проверке».
        SeededSubmission plainPending = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.PENDING);
        // Reviewed — terminal. Not in-flight.
        await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(),
            IssueSubmissionReviewStatus.APPROVED, Guid.NewGuid());

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage inReviewResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/in-review?page=1&pageSize=10&courseId={courseId}");
        inReviewResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse inReview =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(inReviewResponse);

        var item = Assert.Single(inReview.Items);
        Assert.Equal(gated.SubmissionId, item.SubmissionId);
        Assert.Equal("PENDING", item.ReviewStatus);
        Assert.Equal("QUEUED", item.AiReviewStatus);
        Assert.DoesNotContain(inReview.Items, x => x.SubmissionId == plainPending.SubmissionId);

        // Success criterion: the gated submission is NOT in the human «Ожидают проверки» tab.
        HttpResponseMessage pendingResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        pendingResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse pending =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(pendingResponse);

        Assert.DoesNotContain(pending.Items, x => x.SubmissionId == gated.SubmissionId);
        Assert.Contains(pending.Items, x => x.SubmissionId == plainPending.SubmissionId);
    }

    [Fact]
    public async Task GetInReviewIssues_WithoutReviewPermission_ReturnsForbidden()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response =
            await AppHttpClient.GetAsync("/progress/reviews/issues/in-review?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReviewedReviewIssues_GroupsAttemptsByStudentAndIssue_ReturnsLatestWithAttemptsCount()
    {
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        // Один студент, одно задание, две попытки: #1 нужны правки → #2 принято.
        IReadOnlyList<Guid> attempts = await SeedAttemptGroupAsync(
            courseId, projectId, issueId,
            [IssueSubmissionReviewStatus.CHANGES_REQUESTED, IssueSubmissionReviewStatus.APPROVED]);
        Guid latestSubmissionId = attempts[^1];

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage reviewedResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/reviewed?page=1&pageSize=10&courseId={courseId}");
        reviewedResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse reviewed =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(reviewedResponse);

        // Группа схлопнута в ОДНУ карточку: представитель = последняя попытка.
        Assert.Equal(1, reviewed.TotalCount);
        var item = Assert.Single(reviewed.Items);
        Assert.Equal(latestSubmissionId, item.SubmissionId);
        Assert.Equal("APPROVED", item.ReviewStatus);
        Assert.Equal(2, item.SubmissionNo);
        Assert.Equal(2, item.AttemptsCount);

        // Старая попытка #1 НЕ всплывает отдельной карточкой в другом табе.
        HttpResponseMessage pendingResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        pendingResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse pending =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(pendingResponse);
        Assert.Empty(pending.Items);
    }

    [Fact]
    public async Task GetPendingReviewIssues_GroupsAttempts_LatestPendingRepresentsGroup_NotInReviewedTab()
    {
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        // #1 нужны правки (терминальная), #2 свежая отправка ждёт проверки.
        IReadOnlyList<Guid> attempts = await SeedAttemptGroupAsync(
            courseId, projectId, issueId,
            [IssueSubmissionReviewStatus.CHANGES_REQUESTED, IssueSubmissionReviewStatus.PENDING]);
        Guid latestSubmissionId = attempts[^1];

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage pendingResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        pendingResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse pending =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(pendingResponse);

        var item = Assert.Single(pending.Items);
        Assert.Equal(latestSubmissionId, item.SubmissionId);
        Assert.Equal("PENDING", item.ReviewStatus);
        Assert.Equal(2, item.AttemptsCount);

        // Та же группа НЕ дублируется в reviewed-табе из-за старой попытки #1.
        HttpResponseMessage reviewedResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/reviewed?page=1&pageSize=10&courseId={courseId}");
        reviewedResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse reviewed =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(reviewedResponse);
        Assert.Empty(reviewed.Items);
    }

    [Fact]
    public async Task GetInReviewIssues_GroupsAttempts_GatedLatestRepresentsGroup()
    {
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        // #1 нужны правки, #2 свежая отправка прямо сейчас в AI-пайплайне (gated).
        IReadOnlyList<Guid> attempts = await SeedAttemptGroupAsync(
            courseId, projectId, issueId,
            [IssueSubmissionReviewStatus.CHANGES_REQUESTED, IssueSubmissionReviewStatus.PENDING],
            gateLastForAiReview: true);
        Guid latestSubmissionId = attempts[^1];

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage inReviewResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/in-review?page=1&pageSize=10&courseId={courseId}");
        inReviewResponse.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse inReview =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(inReviewResponse);

        var item = Assert.Single(inReview.Items);
        Assert.Equal(latestSubmissionId, item.SubmissionId);
        Assert.Equal(2, item.AttemptsCount);

        // Gated-группа не висит ни в pending, ни в reviewed.
        HttpResponseMessage pendingResponse = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        pendingResponse.EnsureSuccessStatusCode();
        Assert.Empty(
            (await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(pendingResponse)).Items);
    }

    [Fact]
    public async Task GetPendingReviewIssues_SingleAttempt_ReportsAttemptsCountOne()
    {
        Guid courseId = Guid.NewGuid();
        SeededSubmission single = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        response.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse result =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(response);

        var item = Assert.Single(result.Items);
        Assert.Equal(single.SubmissionId, item.SubmissionId);
        Assert.Equal(1, item.AttemptsCount);
    }

    [Fact]
    public async Task GetPendingReviewIssues_CarriesStudentQuestionAt_AfterStudentPrQuestionAsked()
    {
        Guid courseId = Guid.NewGuid();
        SeededSubmission seeded = await SeedSubmissionAsync(
            Guid.NewGuid(), courseId, Guid.NewGuid(), Guid.NewGuid(), IssueSubmissionReviewStatus.PENDING);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        // Baseline: без вопроса StudentQuestionAt == null (получение данных, пустой случай).
        HttpResponseMessage before = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        before.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse beforeResult =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(before);
        Assert.Null(Assert.Single(beforeResult.Items).StudentQuestionAt);

        // #713: студент задал вопрос в своём PR → ARS публикует StudentPrQuestionAsked.
        DateTimeOffset askedAt = DateTimeOffset.UtcNow;
        await InvokeMessageAndWaitAsync(new StudentPrQuestionAsked(
            StudentPrMessageId: Guid.NewGuid(),
            AiReviewId: Guid.NewGuid(),
            SubmissionId: seeded.SubmissionId,
            IssueId: seeded.IssueId,
            CourseId: courseId,
            AuthorId: Guid.NewGuid(),
            StudentUserId: Guid.NewGuid(),
            StudentGithubLogin: "student-gh",
            StudentName: "Student",
            Body: "Не понимаю замечание",
            RepoFullName: "test/repo",
            PullNumber: 7,
            PullRequestUrl: "https://github.com/test/repo/pull/7",
            CommentUrl: "https://github.com/test/repo/pull/7#discussion_r1",
            Path: null,
            Line: null,
            GithubCommentId: 999L,
            CreatedAt: askedAt));

        // Панель «Проверка работ» несёт денорм-timestamp вопроса на карточке сдачи.
        HttpResponseMessage after = await AppHttpClient.GetAsync(
            $"/progress/reviews/issues/pending?page=1&pageSize=10&courseId={courseId}");
        after.EnsureSuccessStatusCode();
        ReviewIssuesPagedResponse afterResult =
            await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(after);

        var item = Assert.Single(afterResult.Items);
        Assert.Equal(seeded.SubmissionId, item.SubmissionId);
        Assert.NotNull(item.StudentQuestionAt);
        Assert.Equal(askedAt.UtcDateTime, item.StudentQuestionAt!.Value, TimeSpan.FromSeconds(2));
    }

    private async Task<SeededSubmission> SeedSubmissionAsync(
        Guid studentId,
        Guid courseId,
        Guid projectId,
        Guid issueId,
        IssueSubmissionReviewStatus reviewStatus,
        Guid? reviewerId = null,
        bool includeProjectProgress = false,
        bool gateForAiReview = false)
    {
        SeededSubmission? seededSubmission = null;

        await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(studentId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            IssueProgress issueProgress = IssueProgress.Create(enrollment.Id, projectId, issueId).Value;
            issueProgress.StartWork();
            issueProgress.SubmitForReview();

            IssueSubmission submission = IssueSubmission.Create(
                issueProgress.Id,
                AttemptNumber.Create(1).Value,
                IssueSubmissionPayload.Create($"https://github.com/test/{Guid.NewGuid():N}/pull/1").Value).Value;

            switch (reviewStatus)
            {
                case IssueSubmissionReviewStatus.IN_REVIEW:
                    submission.StartReview(reviewerId ?? Guid.NewGuid());
                    break;
                case IssueSubmissionReviewStatus.APPROVED:
                    submission.StartReview(reviewerId ?? Guid.NewGuid());
                    submission.Approve(IssueReviewFeedback.Create("Approved").Value);
                    issueProgress.Approve();
                    break;
                case IssueSubmissionReviewStatus.CHANGES_REQUESTED:
                    submission.StartReview(reviewerId ?? Guid.NewGuid());
                    submission.RequestChanges(IssueReviewFeedback.Create("Needs changes").Value);
                    issueProgress.RequestChanges();
                    break;
            }

            // ARS gate: while the AI reviews the PR, the submission is hidden from the
            // human queue (ready_for_human_review=false) and surfaces in the «В проверке» tab.
            if (gateForAiReview)
            {
                submission.GateForAiReview();
                submission.MarkAiQueued();
            }

            await db.CourseEnrollments.AddAsync(enrollment);
            await db.IssueProgresses.AddAsync(issueProgress);
            await db.IssueSubmissions.AddAsync(submission);

            if (includeProjectProgress)
            {
                await db.ProjectProgresses.AddAsync(ProjectProgress.Create(enrollment.Id, projectId, 1).Value);
            }

            await db.SaveChangesAsync();

            seededSubmission = new SeededSubmission(
                enrollment.Id,
                courseId,
                projectId,
                issueId,
                submission.Id);
        });

        return Assert.IsType<SeededSubmission>(seededSubmission);
    }

    /// <summary>
    /// #369: создаёт ОДНУ группу «студент + задание» (один enrollment + issue_progress)
    /// с N попытками (attempt_number 1..N) заданных статусов. Последняя опционально
    /// гейтится под AI-ревью. Возвращает id попыток в порядке возрастания
    /// attempt_number — последняя является представителем группы.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> SeedAttemptGroupAsync(
        Guid courseId,
        Guid projectId,
        Guid issueId,
        IssueSubmissionReviewStatus[] attemptStatuses,
        bool gateLastForAiReview = false)
    {
        List<Guid> submissionIds = [];

        await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(
                Guid.NewGuid(), courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            IssueProgress issueProgress = IssueProgress.Create(enrollment.Id, projectId, issueId).Value;
            issueProgress.StartWork();
            issueProgress.SubmitForReview();

            await db.CourseEnrollments.AddAsync(enrollment);
            await db.IssueProgresses.AddAsync(issueProgress);

            List<IssueSubmission> submissions = [];
            for (int i = 0; i < attemptStatuses.Length; i++)
            {
                IssueSubmission submission = IssueSubmission.Create(
                    issueProgress.Id,
                    AttemptNumber.Create(i + 1).Value,
                    IssueSubmissionPayload.Create($"https://github.com/test/{Guid.NewGuid():N}/pull/1").Value).Value;

                switch (attemptStatuses[i])
                {
                    case IssueSubmissionReviewStatus.IN_REVIEW:
                        submission.StartReview(Guid.NewGuid());
                        break;
                    case IssueSubmissionReviewStatus.APPROVED:
                        submission.StartReview(Guid.NewGuid());
                        submission.Approve(IssueReviewFeedback.Create("Approved").Value);
                        break;
                    case IssueSubmissionReviewStatus.CHANGES_REQUESTED:
                        submission.StartReview(Guid.NewGuid());
                        submission.RequestChanges(IssueReviewFeedback.Create("Needs changes").Value);
                        break;
                }

                if (gateLastForAiReview && i == attemptStatuses.Length - 1)
                {
                    submission.GateForAiReview();
                    submission.MarkAiQueued();
                }

                await db.IssueSubmissions.AddAsync(submission);
                submissions.Add(submission);
            }

            await db.SaveChangesAsync();

            // Id задан в factory через CreateVersion7() — собираем после save
            // для единообразия с SeedSubmissionAsync.
            submissionIds.AddRange(submissions.Select(s => s.Id));
        });

        return submissionIds;
    }

    private sealed record SeededSubmission(
        Guid EnrollmentId,
        Guid CourseId,
        Guid ProjectId,
        Guid IssueId,
        Guid SubmissionId);
}
