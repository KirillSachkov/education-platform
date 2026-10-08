using System.Net;
using ContentAccess;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Courses;

/// <summary>
///     access-derive-model Phase 2: <see cref="CourseEnrollment"/> is a lazily-created progress
///     anchor. A grant-holder with NO pre-existing enrollment row can engage with a course
///     (mark viewed / submit / start) — the anchor is materialized on first interaction, gated by
///     entitlement. A non-entitled user is denied and no anchor is created.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class LazyEnrollmentAnchorTests : ProgressServiceTestsBase
{
    public LazyEnrollmentAnchorTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ---- (a) Mark material viewed → anchor created + module_item_progress cascades ----

    [Fact]
    public async Task MarkMaterialViewed_NoPreExistingEnrollment_EntitledUser_CreatesAnchorAndCascades()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        // Non-admin user → entitlement is decided by FakeEntitlementChecker (GrantAll by default),
        // not bypassed. No pre-existing enrollment row is seeded.
        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        await AssertNoEnrollment(userId, courseId);

        HttpResponseMessage response = await PostAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseEnrollment? anchor = await ExecuteInDb(db =>
            db.CourseEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId));
        ModuleItemProgress? itemProgress = await ExecuteInDb(db =>
            db.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ModuleId == moduleId
                && x.ReferenceId == materialId
                && x.ItemType == ModuleItemProgressType.MATERIAL));

        Assert.NotNull(anchor);
        Assert.Equal(EnrollmentSource.ENGAGEMENT, anchor.Source);
        Assert.Equal(authorId, anchor.AuthorId);
        Assert.NotNull(itemProgress);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, itemProgress.Status);
    }

    [Fact]
    public async Task MarkMaterialViewed_NoPreExistingEnrollment_NonEntitledUser_DeniedAndNoAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyAll();
        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        HttpResponseMessage response = await PostAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int anchorCount = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.UserId == userId && e.CourseId == courseId));
        Assert.Equal(0, anchorCount);
    }

    // ---- (b) Submit issue → anchor created ----

    [Fact]
    public async Task SubmitIssue_NoPreExistingEnrollment_EntitledUser_CreatesAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddProject(courseId, projectId, 1);
        EducationContentClient.AddIssue(projectId, issueId, moduleId: null);

        await AssertNoEnrollment(userId, courseId);

        // First start (also lazily ensure-creates the anchor + IssueProgress), then submit.
        HttpResponseMessage start = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        HttpResponseMessage submit = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        CourseEnrollment? anchor = await ExecuteInDb(db =>
            db.CourseEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId));
        Assert.NotNull(anchor);
        Assert.Equal(EnrollmentSource.ENGAGEMENT, anchor.Source);
        Assert.Equal(authorId, anchor.AuthorId);

        int submissionCount = await ExecuteInDb(async db =>
        {
            Guid issueProgressId = await db.IssueProgresses
                .Where(p => p.EnrollmentId == anchor.Id && p.IssueId == issueId)
                .Select(p => p.Id)
                .FirstAsync();
            return await db.IssueSubmissions.CountAsync(s => s.IssueProgressId == issueProgressId);
        });
        Assert.Equal(1, submissionCount);
    }

    [Fact]
    public async Task SubmitIssue_NonEntitledUser_DeniedAndNoAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyAll();
        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());

        HttpResponseMessage submit = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));

        Assert.Equal(HttpStatusCode.Forbidden, submit.StatusCode);

        int anchorCount = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.UserId == userId && e.CourseId == courseId));
        Assert.Equal(0, anchorCount);
    }

    // ---- (c) Start issue work / start module work → anchor created ----

    [Fact]
    public async Task StartIssueWork_NoPreExistingEnrollment_EntitledUser_CreatesAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddProject(courseId, projectId, 1);
        EducationContentClient.AddModule(courseId, moduleId, 1);
        EducationContentClient.AddIssue(projectId, issueId, moduleId);

        await AssertNoEnrollment(userId, courseId);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseEnrollment? anchor = await ExecuteInDb(db =>
            db.CourseEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId));
        Assert.NotNull(anchor);
        Assert.Equal(EnrollmentSource.ENGAGEMENT, anchor.Source);
        Assert.Equal(authorId, anchor.AuthorId);
    }

    [Fact]
    public async Task StartIssueWork_NonEntitledUser_DeniedAndNoAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyAll();
        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());
        EducationContentClient.AddProject(courseId, projectId, 1);
        EducationContentClient.AddIssue(projectId, issueId, moduleId: null);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int anchorCount = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.UserId == userId && e.CourseId == courseId));
        Assert.Equal(0, anchorCount);
    }

    [Fact]
    public async Task StartModuleWork_NoPreExistingEnrollment_EntitledUser_CreatesAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddModule(courseId, moduleId, 3);

        await AssertNoEnrollment(userId, courseId);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/modules/{moduleId}/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseEnrollment? anchor = await ExecuteInDb(db =>
            db.CourseEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId));
        ModuleProgress? moduleProgress = await ExecuteInDb(db =>
            db.ModuleProgresses.FirstOrDefaultAsync(p => p.ModuleId == moduleId));

        Assert.NotNull(anchor);
        Assert.Equal(EnrollmentSource.ENGAGEMENT, anchor.Source);
        Assert.Equal(authorId, anchor.AuthorId);
        Assert.NotNull(moduleProgress);
    }

    [Fact]
    public async Task StartModuleWork_NonEntitledUser_DeniedAndNoAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyAll();
        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());
        EducationContentClient.AddModule(courseId, moduleId, 3);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/modules/{moduleId}/start");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int anchorCount = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.UserId == userId && e.CourseId == courseId));
        Assert.Equal(0, anchorCount);
    }

    // ---- (e) Start project work → anchor created (review #367: StartProjectWork migrated) ----

    [Fact]
    public async Task StartProjectWork_NoPreExistingEnrollment_EntitledUser_CreatesAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddProject(courseId, projectId, 2);

        await AssertNoEnrollment(userId, courseId);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseEnrollment? anchor = await ExecuteInDb(db =>
            db.CourseEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId));
        ProjectProgress? projectProgress = await ExecuteInDb(db =>
            db.ProjectProgresses.FirstOrDefaultAsync(p => p.ProjectId == projectId));

        Assert.NotNull(anchor);
        Assert.Equal(EnrollmentSource.ENGAGEMENT, anchor.Source);
        Assert.Equal(authorId, anchor.AuthorId);
        Assert.NotNull(projectProgress);
        Assert.Equal(anchor.Id, projectProgress.EnrollmentId);
    }

    [Fact]
    public async Task StartProjectWork_NonEntitledUser_DeniedAndNoAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyAll();
        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());
        EducationContentClient.AddProject(courseId, projectId, 2);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/start");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int anchorCount = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.UserId == userId && e.CourseId == courseId));
        int projectProgressCount = await ExecuteInDb(db =>
            db.ProjectProgresses.CountAsync(p => p.ProjectId == projectId));
        Assert.Equal(0, anchorCount);
        Assert.Equal(0, projectProgressCount);
    }

    // ---- RecordCoursePosition: no anchor (position is user+course-scoped), but entitlement-gated ----

    [Fact]
    public async Task RecordCoursePosition_NoEnrollment_EntitledUser_SucceedsWithoutAnchor()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/position",
            new { entityType = "MATERIAL", entityId = materialId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Position table is user+course-scoped (no enrollment FK) → no anchor materialized.
        int anchorCount = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.UserId == userId && e.CourseId == courseId));
        int positionCount = await ExecuteInDb(db =>
            db.CoursePositions.CountAsync(p => p.UserId == userId && p.CourseId == courseId));

        Assert.Equal(0, anchorCount);
        Assert.Equal(1, positionCount);
    }

    [Fact]
    public async Task RecordCoursePosition_NonEntitledUser_Denied()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyAll();
        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/position",
            new { entityType = "MATERIAL", entityId = materialId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int positionCount = await ExecuteInDb(db =>
            db.CoursePositions.CountAsync(p => p.UserId == userId && p.CourseId == courseId));
        Assert.Equal(0, positionCount);
    }

    // ---- (d) Leaderboard invariant: XP earned via a lazy path ranks the user (doc §8 Q4) ----

    [Fact]
    public async Task LazyEngagement_EarnsXp_AndAppearsOnAuthorLeaderboard()
    {
        // Proves "every XP path ensure-creates the anchor": a grant-holder with NO pre-existing
        // enrollment marks a material viewed → MATERIAL_VIEWED XP (enrollment_id=null) + an
        // ENGAGEMENT anchor carrying author_id. The author-scoped leaderboard finds the user via
        // the anchor (author_users CTE) and sums the user-scoped XP branch → ranked.
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        HttpResponseMessage viewed = await PostAsync($"/progress/materials/{materialId}/view");
        Assert.Equal(HttpStatusCode.OK, viewed.StatusCode);

        // Invariant pre-checks: anchor exists with the right author + a user-scoped XP award landed.
        CourseEnrollment? anchor = await ExecuteInDb(db =>
            db.CourseEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId));
        Assert.NotNull(anchor);
        Assert.Equal(authorId, anchor.AuthorId);

        XpAward? award = await ExecuteInDb(db =>
            db.XpAwards.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.AwardType == XpAwardType.MATERIAL_VIEWED && x.SourceId == materialId));
        Assert.NotNull(award);
        Assert.Null(award.EnrollmentId);

        // Author-scoped leaderboard must rank the user.
        HttpResponseMessage leaderboard = await AppHttpClient.GetAsync(
            $"/progress/leaderboard?page=1&pageSize=10&authorId={authorId}");
        Assert.Equal(HttpStatusCode.OK, leaderboard.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(leaderboard);

        // The lazy-created anchor makes the user discoverable by the author-scoped leaderboard;
        // their user-scoped (enrollment_id=null) MATERIAL_VIEWED XP — plus MODULE_COMPLETED for the
        // single-item module the view also completed — is summed → ranked. The exact total is
        // secondary; the invariant is "earned XP via a lazy path ⇒ ranked".
        Assert.Equal(1, result.TotalCount);
        Assert.Contains(result.Items, i => i.UserId == userId && i.TotalXp > 0);
        Assert.NotNull(result.CurrentUser);
        Assert.Equal(userId, result.CurrentUser!.UserId);
    }

    private async Task AssertNoEnrollment(Guid userId, Guid courseId)
    {
        int existing = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(e => e.UserId == userId && e.CourseId == courseId));
        Assert.Equal(0, existing);
    }
}
