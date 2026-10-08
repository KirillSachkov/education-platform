using AuthService.Contracts;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>ai_review.oversized_skipped</c> (#546):
/// авто-ран AI-проверки пропущен (большой PR) → автору курса приходит одно уведомление
/// «Большой PR — запустите AI-проверку вручную», студенту — ничего. Идемпотентность
/// по <c>AiReviewId</c> (второй guard поверх ARS-side дедупа).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class AiReviewOversizedSkippedHandlerTests : NotificationServiceTestsBase
{
    public AiReviewOversizedSkippedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task OversizedSkipped_NotifiesAuthor_NotStudent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid aiReviewId = Guid.NewGuid();

        StubIssueLookup(issueId, "Решить DS-9");
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Евгений Те", "etetudyyd", "e@e.com", null)]));

        await InvokeMessageAndWaitAsync(new AiReviewOversizedSkipped(
            AiReviewId: aiReviewId,
            SubmissionId: Guid.NewGuid(),
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            RepoFullName: "etetudyyd/DirectoryService",
            PullNumber: 56,
            OccurredAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.AiReviewOversizedSkipped, notification.Type);
        Assert.Equal(aiReviewId, notification.CorrelationId);
        Assert.Contains("Евгений Те", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Решить DS-9", notification.Body, StringComparison.Ordinal);
        Assert.Contains("etetudyyd/DirectoryService#56", notification.Body, StringComparison.Ordinal);
        // Dispatcher запекает targetUrl через PlatformLinkBuilder — клик ведёт на
        // author review-страницу, где кнопка «Перепроверить» запускает полный прогон.
        Assert.Contains("/author/review", notification.Payload, StringComparison.Ordinal);

        int studentSideCount = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == studentId));
        Assert.Equal(0, studentSideCount);
    }

    [Fact]
    public async Task OversizedSkipped_Twice_IsIdempotent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        StubIssueLookup(issueId, "Задача");
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Студент", "stud", "s@e.com", null)]));

        AiReviewOversizedSkipped evt = new(
            AiReviewId: Guid.NewGuid(),
            SubmissionId: Guid.NewGuid(),
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            RepoFullName: "stud/repo",
            PullNumber: 7,
            OccurredAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(1, count);
    }

    private void StubIssueLookup(Guid issueId, string title)
    {
        EducationContentClient.GetIssueSearchLookupAsync(issueId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IssueSearchLookupDto, Error>(new IssueSearchLookupDto(
                Id: issueId,
                ProjectId: Guid.NewGuid(),
                CourseId: null,
                CourseSlug: null,
                Title: title,
                ModuleId: null,
                CourseTitle: null,
                ProjectTitle: null,
                ModuleTitle: null,
                Status: PublicationStatus.PUBLISHED,
                RequiredAccessTags: [],
                UpdatedAt: DateTime.UtcNow)));
    }
}
