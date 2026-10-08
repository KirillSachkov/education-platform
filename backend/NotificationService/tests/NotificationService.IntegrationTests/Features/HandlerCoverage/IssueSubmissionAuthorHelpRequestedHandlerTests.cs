using AuthService.Contracts;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>issue_submission.author_help_requested</c> (#383):
/// студент нажал «Позвать автора» → автору курса приходит одно уведомление
/// «Студенту нужна помощь», ученику — ничего. Идемпотентность по <c>SubmissionId</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class IssueSubmissionAuthorHelpRequestedHandlerTests : NotificationServiceTestsBase
{
    public IssueSubmissionAuthorHelpRequestedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AuthorHelpRequested_NotifiesAuthor_NotStudent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid submissionId = Guid.NewGuid();

        StubIssueLookup(issueId, "Решить DS-4");
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Ульяна Ро", "uluanaro", "u@e.com", null)]));

        await InvokeMessageAndWaitAsync(new IssueSubmissionAuthorHelpRequested(
            SubmissionId: submissionId,
            IssueProgressId: Guid.NewGuid(),
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            CourseId: courseId,
            RequestedAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.AuthorHelpRequested, notification.Type);
        Assert.Equal(submissionId, notification.CorrelationId);
        Assert.Contains("Ульяна Ро", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Решить DS-4", notification.Body, StringComparison.Ordinal);

        int studentSideCount = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == studentId));
        Assert.Equal(0, studentSideCount);
    }

    [Fact]
    public async Task AuthorHelpRequested_WithMessageAndTelegram_TelegramBodyIncludesContactAndMessage()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        StubIssueLookup(issueId, "Решить DS-7");
        // #575 — студент с привязанным telegram-ником, помощь с текстом.
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Дмитрий", "dim", "d@e.com", null, "cool_dev")]));

        const string message = "Не проходит тест на пагинацию";
        await InvokeMessageAndWaitAsync(new IssueSubmissionAuthorHelpRequested(
            SubmissionId: Guid.NewGuid(),
            IssueProgressId: Guid.NewGuid(),
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            CourseId: Guid.NewGuid(),
            RequestedAt: DateTimeOffset.UtcNow,
            Message: message));

        NotificationCreated published = OutboxCollector.OfType<NotificationCreated>()
            .Single(n => n.RecipientUserId == authorId);

        // Telegram-тело несёт кликабельную t.me-ссылку студента + текст просьбы (#575).
        Assert.NotNull(published.TelegramBody);
        Assert.Contains("t.me/cool_dev", published.TelegramBody!, StringComparison.Ordinal);
        Assert.Contains(message, published.TelegramBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorHelpRequested_Twice_IsIdempotent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid submissionId = Guid.NewGuid();

        StubIssueLookup(issueId, "Задача");
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Студент", "stud", "s@e.com", null)]));

        IssueSubmissionAuthorHelpRequested evt = new(
            SubmissionId: submissionId,
            IssueProgressId: Guid.NewGuid(),
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            CourseId: Guid.NewGuid(),
            RequestedAt: DateTimeOffset.UtcNow);

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
