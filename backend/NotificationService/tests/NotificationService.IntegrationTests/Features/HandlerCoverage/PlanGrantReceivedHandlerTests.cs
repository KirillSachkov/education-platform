using CSharpFunctionalExtensions;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для plan-grant.created (issue #230 TEST-2). Заменяет per-course
/// CourseEnrolled-уведомления одним «вы получили доступ по плану».
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PlanGrantReceivedHandlerTests : NotificationServiceTestsBase
{
    public PlanGrantReceivedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PlanGrantCreated_FullAll_CreatesSinglePlanGrantNotification()
    {
        Guid userId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(BuildEvent(userId, grantId, planTier: "FULL_ALL", courseId: null));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Equal(grantId, notification.CorrelationId);
        Assert.Contains("полный доступ", notification.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".NET Fullstack", notification.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanGrantCreated_Course_RendersCourseAccessSummary()
    {
        Guid userId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(BuildEvent(
            userId,
            grantId,
            planTier: "COURSE",
            courseId: Guid.NewGuid()));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Contains("курсу", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanGrantCreated_Intensive_NamesPlanAndDeepLinksToCourse()
    {
        // #485: тело называет конкретный продукт (offer-aware noun + PlanName) без
        // onboarding-чеклиста, а клик ведёт на страницу курса (slug через ECS).
        Guid userId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId,
                Slug: "aspnet-architecture",
                Title: "Архитектура .NET веб приложений",
                Description: "Интенсив",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: Guid.NewGuid())));

        await InvokeMessageAndWaitAsync(BuildEvent(
            userId,
            grantId,
            planTier: "COURSE",
            courseId: courseId,
            planName: "Архитектура .NET веб приложений",
            offerType: "INTENSIVE",
            courseIds: [courseId]));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Contains(
            "интенсиву «Архитектура .NET веб приложений»",
            notification.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Вступите", notification.Body, StringComparison.Ordinal);
        // targetUrl запечён в payload диспатчером (PlatformLinkBuilder) — deep-link на курс.
        Assert.Contains("/courses/aspnet-architecture", notification.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanGrantCreated_FullAll_DeepLinksToHome()
    {
        // Full-access план не привязан к одному курсу — клик ведёт на /home (не на лендинг "/").
        Guid userId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(BuildEvent(
            userId,
            Guid.NewGuid(),
            planTier: "FULL_ALL",
            courseId: null,
            planName: "Полный доступ"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Contains("/home", notification.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanGrantCreated_Twice_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();
        PlanGrantCreated evt = BuildEvent(userId, grantId, planTier: "LEARN_ALL", courseId: null);

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId));

        Assert.Equal(1, count);
    }

    private static PlanGrantCreated BuildEvent(
        Guid userId,
        Guid grantId,
        string planTier,
        Guid? courseId,
        string? planName = null,
        string? offerType = null,
        IReadOnlyList<Guid>? courseIds = null) =>
        new(
            GrantId: grantId,
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: planTier,
            PlanAuthorId: Guid.NewGuid(),
            CourseId: courseId,
            IncludesFutureContent: planTier is "FULL_ALL" or "LEARN_ALL",
            Source: "ADMIN_GRANT",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            CourseIds: courseIds,
            PlanName: planName,
            OfferType: offerType);
}
