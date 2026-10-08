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
/// L1 handler coverage для <c>issue.author_question_asked</c> (#693): студент задал приватный
/// вопрос автору по заданию ДО сабмишена → автору курса приходит одно уведомление
/// «Вопрос по заданию», ученику — ничего. Идемпотентность по <c>QuestionId</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class IssueAuthorQuestionAskedHandlerTests : NotificationServiceTestsBase
{
    public IssueAuthorQuestionAskedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AuthorQuestion_NotifiesAuthor_NotStudent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid questionId = Guid.NewGuid();

        StubIssueLookup(issueId, "Решить DS-4", "dotnet-fullstack");
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Ульяна Ро", "uluanaro", "u@e.com", null)]));

        await InvokeMessageAndWaitAsync(new IssueAuthorQuestionAsked(
            QuestionId: questionId,
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            CourseId: courseId,
            Message: "Не понимаю условие про идемпотентность",
            AskedAt: DateTimeOffset.UtcNow));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.IssueAuthorQuestion, notification.Type);
        Assert.Equal(questionId, notification.CorrelationId);
        Assert.Contains("Ульяна Ро", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Решить DS-4", notification.Body, StringComparison.Ordinal);

        int studentSideCount = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == studentId));
        Assert.Equal(0, studentSideCount);
    }

    [Fact]
    public async Task AuthorQuestion_WithTelegram_TelegramBodyIncludesQuestionAndContact()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        StubIssueLookup(issueId, "Решить DS-7", "dotnet-fullstack");
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Дмитрий", "dim", "d@e.com", null, "cool_dev")]));

        const string question = "Не проходит тест на пагинацию";
        await InvokeMessageAndWaitAsync(new IssueAuthorQuestionAsked(
            QuestionId: Guid.NewGuid(),
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            CourseId: Guid.NewGuid(),
            Message: question,
            AskedAt: DateTimeOffset.UtcNow));

        NotificationCreated published = OutboxCollector.OfType<NotificationCreated>()
            .Single(n => n.RecipientUserId == authorId);

        Assert.NotNull(published.TelegramBody);
        Assert.Contains("t.me/cool_dev", published.TelegramBody!, StringComparison.Ordinal);
        Assert.Contains(question, published.TelegramBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorQuestion_Twice_IsIdempotent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid questionId = Guid.NewGuid();

        StubIssueLookup(issueId, "Задача", null);
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, "Студент", "stud", "s@e.com", null)]));

        IssueAuthorQuestionAsked evt = new(
            QuestionId: questionId,
            StudentUserId: studentId,
            AuthorId: authorId,
            IssueId: issueId,
            CourseId: Guid.NewGuid(),
            Message: "вопрос",
            AskedAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(1, count);
    }

    private void StubIssueLookup(Guid issueId, string title, string? courseSlug)
    {
        EducationContentClient.GetIssueSearchLookupAsync(issueId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IssueSearchLookupDto, Error>(new IssueSearchLookupDto(
                Id: issueId,
                ProjectId: Guid.NewGuid(),
                CourseId: courseSlug is null ? null : Guid.NewGuid(),
                CourseSlug: courseSlug,
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
