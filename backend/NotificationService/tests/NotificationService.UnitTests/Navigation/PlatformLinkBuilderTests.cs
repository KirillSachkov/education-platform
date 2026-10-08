using Shared.Navigation;

namespace NotificationService.UnitTests.Navigation;

public sealed class PlatformLinkBuilderTests
{
    // Phase 5 (#308) — flat URLs, no /@slug prefix. `authorSlug` retained in
    // payload fixtures to verify the parser silently ignores legacy fields.
    private const string BaseUrl = "https://sachkov-learn.net";
    private const string LegacySlug = "sachkov";
    private const string CourseSlug = "dotnet-fullstack";

    [Fact]
    public void BuildOpenUrl_ReturnsShortProxyLink()
    {
        Guid id = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000042");
        string url = PlatformLinkBuilder.BuildOpenUrl(BaseUrl, id);
        Assert.Equal($"{BaseUrl}/n/{id}", url);
    }

    [Fact]
    public void BuildOpenUrl_TrimsTrailingSlashOnBase()
    {
        Guid id = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000042");
        string url = PlatformLinkBuilder.BuildOpenUrl(BaseUrl + "/", id);
        Assert.Equal($"{BaseUrl}/n/{id}", url);
    }

    [Fact]
    public void BuildTargetUrl_Welcome_ReturnsRoot()
    {
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.WELCOME, "{}");
        Assert.Equal($"{BaseUrl}/", url);
    }

    [Fact]
    public void BuildTargetUrl_CourseEnrolled_BuildsFlatCourseUrl()
    {
        Guid courseId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000001");
        string payload = $"{{\"courseId\":\"{courseId}\",\"courseSlug\":\"{CourseSlug}\",\"authorSlug\":\"{LegacySlug}\"}}";
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.COURSE_ENROLLED, payload);
        Assert.Equal($"{BaseUrl}/courses/{CourseSlug}", url);
    }

    [Fact]
    public void BuildTargetUrl_MaterialPublished_BuildsFlatLearnUrl()
    {
        Guid courseId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000001");
        Guid materialId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000002");
        string payload = $"{{\"courseId\":\"{courseId}\",\"courseSlug\":\"{CourseSlug}\",\"authorSlug\":\"{LegacySlug}\",\"materialId\":\"{materialId}\"}}";
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.MATERIAL_PUBLISHED, payload);
        Assert.Equal($"{BaseUrl}/courses/{CourseSlug}/learn/{materialId}", url);
    }

    [Fact]
    public void BuildTargetUrl_IssueApproved_BuildsFlatIssueUrl()
    {
        Guid courseId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000001");
        Guid issueId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000003");
        string payload = $"{{\"courseId\":\"{courseId}\",\"courseSlug\":\"{CourseSlug}\",\"authorSlug\":\"{LegacySlug}\",\"issueId\":\"{issueId}\"}}";
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.ISSUE_SUBMISSION_APPROVED, payload);
        Assert.Equal($"{BaseUrl}/courses/{CourseSlug}/issues/{issueId}", url);
    }

    [Theory]
    [InlineData(NotificationTypes.ISSUE_SUBMISSION_AWAITING_REVIEW)]
    [InlineData(NotificationTypes.AUTHOR_HELP_REQUESTED)]
    [InlineData(NotificationTypes.AI_REVIEW_OVERSIZED_SKIPPED)]
    [InlineData(NotificationTypes.STUDENT_PR_QUESTION_ASKED)]
    public void BuildTargetUrl_ReviewNotification_DeepLinksToSubmission(short notificationType)
    {
        Guid submissionId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000004");
        string payload = $"{{\"submissionId\":\"{submissionId}\"}}";
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, notificationType, payload);
        Assert.Equal($"{BaseUrl}/author/review?submissionId={submissionId}", url);
    }

    [Fact]
    public void BuildTargetUrl_ReviewNotificationWithoutSubmission_FallsBackToReviewList()
    {
        string url = PlatformLinkBuilder.BuildTargetUrl(
            BaseUrl,
            NotificationTypes.AUTHOR_HELP_REQUESTED,
            "{}");

        Assert.Equal($"{BaseUrl}/author/review", url);
    }

    [Fact]
    public void BuildTargetUrl_TelegramLinked_ReturnsSettings()
    {
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.TELEGRAM_LINKED, "{}");
        Assert.Equal($"{BaseUrl}/settings", url);
    }

    [Fact]
    public void BuildTargetUrl_CommentRepliedOnMaterial_IncludesCommentAnchorOnFlatUrl()
    {
        Guid courseId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000001");
        Guid materialId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000002");
        Guid commentId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000005");
        string payload = $"{{\"courseId\":\"{courseId}\",\"courseSlug\":\"{CourseSlug}\",\"authorSlug\":\"{LegacySlug}\",\"entityType\":\"material\",\"entityId\":\"{materialId}\",\"commentId\":\"{commentId}\"}}";
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.COMMENT_REPLIED, payload);
        Assert.Equal($"{BaseUrl}/courses/{CourseSlug}/learn/{materialId}?comment={commentId}", url);
    }

    [Fact]
    public void BuildTargetUrl_BrokenPayload_FallsBackToRoot()
    {
        // Некорректный JSON — не падаем, fallback на корень.
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.MATERIAL_PUBLISHED, "{not valid json");
        Assert.Equal($"{BaseUrl}/", url);
    }

    [Fact]
    public void BuildTargetUrl_MissingRequiredKey_FallsBackToRoot()
    {
        // Payload есть, но без required-ключей — fallback (вместо crash).
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.COURSE_ENROLLED, "{\"other\":\"value\"}");
        Assert.Equal($"{BaseUrl}/", url);
    }

    [Fact]
    public void BuildTargetUrl_AccessExpired_ReturnsPricing()
    {
        // #687 — доступ истёк: клик ведёт в каталог планов для продления.
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, NotificationTypes.ACCESS_EXPIRED, "{}");
        Assert.Equal($"{BaseUrl}/pricing", url);
    }

    [Theory]
    [InlineData(NotificationTypes.SUBSCRIPTION_RENEWED)]
    [InlineData(NotificationTypes.SUBSCRIPTION_RENEWAL_PROBLEM)]
    [InlineData(NotificationTypes.SUBSCRIPTION_RENEWAL_CANCELLED)]
    public void BuildTargetUrl_SubscriptionLifecycle_ReturnsMyPlans(short notificationType)
    {
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, notificationType, "{}");

        Assert.Equal($"{BaseUrl}/my-plans", url);
    }

    [Fact]
    public void BuildTargetUrl_UnknownType_FallsBackToRoot()
    {
        string url = PlatformLinkBuilder.BuildTargetUrl(BaseUrl, 999, "{}");
        Assert.Equal($"{BaseUrl}/", url);
    }

    [Fact]
    public void ResolveTargetUrl_UsesBakedTargetUrl()
    {
        string payload = $"{{\"targetUrl\":\"{BaseUrl}/courses/{CourseSlug}\"}}";
        string url = PlatformLinkBuilder.ResolveTargetUrl(BaseUrl, NotificationTypes.COURSE_ENROLLED, payload);
        Assert.Equal($"{BaseUrl}/courses/{CourseSlug}", url);
    }

    [Fact]
    public void ResolveTargetUrl_ExistingReviewNotification_RebuildsSubmissionDeepLink()
    {
        Guid submissionId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000004");
        string payload =
            $"{{\"submissionId\":\"{submissionId}\",\"targetUrl\":\"{BaseUrl}/author/review\"}}";

        string url = PlatformLinkBuilder.ResolveTargetUrl(
            BaseUrl,
            NotificationTypes.AUTHOR_HELP_REQUESTED,
            payload);

        Assert.Equal($"{BaseUrl}/author/review?submissionId={submissionId}", url);
    }

    [Fact]
    public void ResolveTargetUrl_MissingBakedTargetUrl_FallsBackToRoot()
    {
        Guid courseId = Guid.Parse("c7b8a9d1-1111-4000-8000-000000000001");
        string payload = $"{{\"courseId\":\"{courseId}\",\"courseSlug\":\"{CourseSlug}\",\"authorSlug\":\"{LegacySlug}\"}}";

        string url = PlatformLinkBuilder.ResolveTargetUrl(BaseUrl, NotificationTypes.COURSE_ENROLLED, payload);

        Assert.Equal($"{BaseUrl}/", url);
    }
}
