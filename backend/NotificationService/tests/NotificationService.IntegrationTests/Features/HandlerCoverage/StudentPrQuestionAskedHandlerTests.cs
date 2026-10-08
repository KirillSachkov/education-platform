using AuthService.Contracts;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>student_pr_question.asked</c> (#713, epic pr-dialogue): студент
/// задал вопрос reply'ем в своём GitHub-PR → автору курса приходит одно уведомление «Вопрос по PR
/// от студента», студенту — ничего. Идемпотентность по <c>StudentPrMessageId</c>. GitHub-only автор
/// PR (<c>StudentUserId=null</c>) уведомляет автора без t.me-контакта, не дёргая AuthService.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class StudentPrQuestionAskedHandlerTests : NotificationServiceTestsBase
{
    public StudentPrQuestionAskedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PrQuestion_NotifiesAuthor_NotStudent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();

        StubStudent(studentId, "Евгений Те", "etetudyyd", telegram: null);

        await InvokeMessageAndWaitAsync(Event(
            messageId: messageId,
            authorId: authorId,
            studentUserId: studentId,
            studentName: "Евгений Те",
            body: "Почему не проходит тест на пагинацию?"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.StudentPrQuestionAsked, notification.Type);
        Assert.Equal(messageId, notification.CorrelationId);
        Assert.Contains("Евгений Те", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Почему не проходит тест на пагинацию?", notification.Body, StringComparison.Ordinal);
        Assert.Contains("owner/repo#56", notification.Body, StringComparison.Ordinal);
        // Dispatcher запекает targetUrl через PlatformLinkBuilder — клик ведёт на панель проверки,
        // где автор увидит вопрос и ответит.
        Assert.Contains("/author/review", notification.Payload, StringComparison.Ordinal);

        int studentSideCount = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == studentId));
        Assert.Equal(0, studentSideCount);
    }

    [Fact]
    public async Task PrQuestion_WithTelegram_TelegramBodyIncludesQuestionContactAndThreadLink()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        StubStudent(studentId, "Дмитрий", "dim", telegram: "cool_dev");

        const string question = "Не понимаю условие про идемпотентность";
        const string commentUrl = "https://github.com/owner/repo/pull/56#discussion_r777";

        await InvokeMessageAndWaitAsync(Event(
            messageId: Guid.NewGuid(),
            authorId: authorId,
            studentUserId: studentId,
            studentName: "Дмитрий",
            body: question,
            commentUrl: commentUrl));

        NotificationCreated published = OutboxCollector.OfType<NotificationCreated>()
            .Single(n => n.RecipientUserId == authorId);

        Assert.NotNull(published.TelegramBody);
        Assert.Contains(question, published.TelegramBody!, StringComparison.Ordinal);
        Assert.Contains("t.me/cool_dev", published.TelegramBody!, StringComparison.Ordinal);
        Assert.Contains(commentUrl, published.TelegramBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrQuestion_GithubOnlyStudent_NotifiesAuthorWithoutContact()
    {
        Guid authorId = Guid.NewGuid();

        // StudentUserId=null → AuthService НЕ дёргается; имя берётся из github-логина.
        await InvokeMessageAndWaitAsync(Event(
            messageId: Guid.NewGuid(),
            authorId: authorId,
            studentUserId: null,
            studentName: null,
            body: "Вопрос от внешнего контрибьютора"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.StudentPrQuestionAsked, notification.Type);
        Assert.Contains("octocat", notification.Body, StringComparison.Ordinal);
        await AuthServiceClient.DidNotReceive().GetUsersByIdsAsync(
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrQuestion_AuthLookupFails_StillNotifiesAuthorWithoutContact()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        // AuthService недоступен → graceful fallback: уведомление автору всё равно доходит,
        // просто без t.me-контакта. Имя берётся из события.
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<AuthUserLookupDto>, Error>(
                Error.Failure("auth.unavailable", "Auth service down")));

        await InvokeMessageAndWaitAsync(Event(
            messageId: Guid.NewGuid(),
            authorId: authorId,
            studentUserId: studentId,
            studentName: "Мария Ро",
            body: "Как оформить PR?"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.StudentPrQuestionAsked, notification.Type);
        Assert.Contains("Мария Ро", notification.Body, StringComparison.Ordinal);

        NotificationCreated published = OutboxCollector.OfType<NotificationCreated>()
            .Single(n => n.RecipientUserId == authorId);
        Assert.NotNull(published.TelegramBody);
        Assert.DoesNotContain("t.me/", published.TelegramBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrQuestion_Twice_IsIdempotent()
    {
        Guid studentId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        StubStudent(studentId, "Студент", "stud", telegram: null);

        StudentPrQuestionAsked evt = Event(
            messageId: Guid.NewGuid(),
            authorId: authorId,
            studentUserId: studentId,
            studentName: "Студент",
            body: "вопрос");

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PrQuestion_SameSubmissionUnread_CoalescesToOnePush()
    {
        // #713 FIX-5: два вопроса по ОДНОЙ сдаче (разные messageId), автор ещё не прочитал →
        // создаётся ОДНО уведомление, второй пуш коалесится (защита от флуда).
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid submissionId = Guid.NewGuid();

        StubStudent(studentId, "Студент", "stud", telegram: null);

        await InvokeMessageAndWaitAsync(Event(
            messageId: Guid.NewGuid(),
            authorId: authorId,
            studentUserId: studentId,
            studentName: "Студент",
            body: "первый вопрос",
            submissionId: submissionId));
        await InvokeMessageAndWaitAsync(Event(
            messageId: Guid.NewGuid(),
            authorId: authorId,
            studentUserId: studentId,
            studentName: "Студент",
            body: "второй вопрос",
            submissionId: submissionId));

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == authorId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PrQuestion_DifferentSubmission_CreatesSeparatePush()
    {
        // #713 FIX-5: вопросы по РАЗНЫМ сдачам не коалесятся — автор получает отдельный пуш.
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();

        StubStudent(studentId, "Студент", "stud", telegram: null);

        await InvokeMessageAndWaitAsync(Event(
            messageId: Guid.NewGuid(),
            authorId: authorId,
            studentUserId: studentId,
            studentName: "Студент",
            body: "вопрос по сдаче A",
            submissionId: Guid.NewGuid()));
        await InvokeMessageAndWaitAsync(Event(
            messageId: Guid.NewGuid(),
            authorId: authorId,
            studentUserId: studentId,
            studentName: "Студент",
            body: "вопрос по сдаче B",
            submissionId: Guid.NewGuid()));

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == authorId));
        Assert.Equal(2, count);
    }

    private void StubStudent(Guid studentId, string name, string username, string? telegram)
    {
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(c => c.Contains(studentId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(studentId, name, username, "s@e.com", null, telegram)]));
    }

    private static StudentPrQuestionAsked Event(
        Guid messageId,
        Guid authorId,
        Guid? studentUserId,
        string? studentName,
        string body,
        string? commentUrl = null,
        Guid? submissionId = null)
    {
        return new StudentPrQuestionAsked(
            StudentPrMessageId: messageId,
            AiReviewId: Guid.NewGuid(),
            SubmissionId: submissionId ?? Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            CourseId: null,
            AuthorId: authorId,
            StudentUserId: studentUserId,
            StudentGithubLogin: "octocat",
            StudentName: studentName,
            Body: body,
            RepoFullName: "owner/repo",
            PullNumber: 56,
            PullRequestUrl: "https://github.com/owner/repo/pull/56",
            CommentUrl: commentUrl ?? "https://github.com/owner/repo/pull/56#discussion_r1",
            Path: null,
            Line: null,
            GithubCommentId: 12345,
            CreatedAt: DateTimeOffset.UtcNow);
    }
}
