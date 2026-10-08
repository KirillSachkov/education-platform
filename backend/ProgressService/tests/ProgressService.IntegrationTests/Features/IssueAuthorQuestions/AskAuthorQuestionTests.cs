using System.Net;
using System.Net.Http.Json;
using ContentAccess;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.AuthorQuestions;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace ProgressService.IntegrationTests.Features.IssueAuthorQuestions;

/// <summary>
///     #693 «Задать вопрос автору» (issue-scoped, до сабмишена). Покрывает write-эндпоинт
///     <c>POST /progress/issues/{id}/ask-author/</c> (happy / идемпотентность / 401 / 403 /
///     валидация / graceful ECS), GET-состояние и hard-delete cascade.
/// </summary>
public sealed class AskAuthorQuestionTests : ProgressServiceTestsBase
{
    public AskAuthorQuestionTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private static string AskUrl(Guid issueId) => $"/progress/issues/{issueId}/ask-author/";
    private static string StateUrl(Guid issueId) => $"/progress/issues/{issueId}/author-question/";

    [Fact]
    public async Task Ask_FirstTime_Returns200_PersistsRow_AndPublishesEventOnce()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        EducationContentClient.AddEntityOwnership("issue", issueId, courseId, authorId);
        EducationContentClient.AddIssueCourseBinding(issueId, courseId, "dotnet-fullstack");
        AuthenticateAs(userId, "platform-participant");
        NoOpOutboxService.Reset();

        const string message = "Не понимаю формулировку про идемпотентность — поясните, пожалуйста.";
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            IssueAuthorQuestion q = await db.IssueAuthorQuestions
                .SingleAsync(x => x.UserId == userId && x.IssueId == issueId);
            Assert.Equal(message, q.Message);
            Assert.NotEqual(default, q.AskedAt);
        });

        IssueAuthorQuestionAsked published = NoOpOutboxService.Published
            .OfType<IssueAuthorQuestionAsked>()
            .Single();
        Assert.Equal(userId, published.StudentUserId);
        Assert.Equal(authorId, published.AuthorId);
        Assert.Equal(issueId, published.IssueId);
        Assert.Equal(courseId, published.CourseId);
        Assert.Equal(message, published.Message);
    }

    [Fact]
    public async Task Ask_TrimsMessage()
    {
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AskUrl(issueId), new { message = "  есть вопрос  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ExecuteInDb(async db =>
        {
            IssueAuthorQuestion q = await db.IssueAuthorQuestions
                .SingleAsync(x => x.UserId == userId && x.IssueId == issueId);
            Assert.Equal("есть вопрос", q.Message);
        });
        Assert.Equal("есть вопрос", NoOpOutboxService.Published.OfType<IssueAuthorQuestionAsked>().Single().Message);
    }

    [Fact]
    public async Task Ask_SecondTime_IsIdempotent_DoesNotPublishTwice()
    {
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");
        NoOpOutboxService.Reset();

        HttpResponseMessage first = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message = "первый" });
        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message = "второй" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Одна строка, исходный текст сохранён (first-write-wins), событие ровно одно.
        await ExecuteInDb(async db =>
        {
            IssueAuthorQuestion q = await db.IssueAuthorQuestions
                .SingleAsync(x => x.UserId == userId && x.IssueId == issueId);
            Assert.Equal("первый", q.Message);
        });
        Assert.Single(NoOpOutboxService.Published.OfType<IssueAuthorQuestionAsked>());
    }

    [Fact]
    public async Task Ask_Anonymous_Returns401()
    {
        Guid issueId = Guid.NewGuid();
        RemoveAuthentication();
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message = "вопрос" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(NoOpOutboxService.Published.OfType<IssueAuthorQuestionAsked>());
    }

    [Fact]
    public async Task Ask_WithoutEntitlement_Returns403_AndNoRowNoEvent()
    {
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyResourceType(ResourceTypes.ISSUE);
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message = "вопрос" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(NoOpOutboxService.Published.OfType<IssueAuthorQuestionAsked>());
        int rows = await ExecuteInDb(db => db.IssueAuthorQuestions.CountAsync(x => x.IssueId == issueId));
        Assert.Equal(0, rows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ask_BlankMessage_Returns400(string message)
    {
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(NoOpOutboxService.Published.OfType<IssueAuthorQuestionAsked>());
        int rows = await ExecuteInDb(db => db.IssueAuthorQuestions.CountAsync(x => x.IssueId == issueId));
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task Ask_MessageTooLong_Returns400()
    {
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");
        NoOpOutboxService.Reset();

        string tooLong = new('a', IssueAuthorQuestion.MESSAGE_MAX_LENGTH + 1);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message = tooLong });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(NoOpOutboxService.Published.OfType<IssueAuthorQuestionAsked>());
    }

    [Fact]
    public async Task Ask_WhenEcsOwnershipUnavailable_StillSucceeds_NotFiveHundred()
    {
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.SetEntityOwnershipUnavailable();
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message = "вопрос" });

        // Graceful: ECS недоступен → не 500. Запись создаётся, событие публикуется с пустым автором.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        int rows = await ExecuteInDb(db => db.IssueAuthorQuestions.CountAsync(x => x.IssueId == issueId));
        Assert.Equal(1, rows);
        IssueAuthorQuestionAsked published = NoOpOutboxService.Published.OfType<IssueAuthorQuestionAsked>().Single();
        Assert.Equal(Guid.Empty, published.AuthorId);
        Assert.Null(published.CourseId);
    }

    [Fact]
    public async Task GetState_BeforeAsk_ReturnsNull_AfterAsk_ReturnsTimestamp()
    {
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage before = await AppHttpClient.GetAsync(StateUrl(issueId));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        AuthorQuestionStateResponse beforeState = await ReadWrappedResultAsync<AuthorQuestionStateResponse>(before);
        Assert.Null(beforeState.AskedAt);

        HttpResponseMessage ask = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message = "вопрос" });
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);

        HttpResponseMessage after = await AppHttpClient.GetAsync(StateUrl(issueId));
        AuthorQuestionStateResponse afterState = await ReadWrappedResultAsync<AuthorQuestionStateResponse>(after);
        Assert.NotNull(afterState.AskedAt);
    }

    [Fact]
    public async Task GetState_Anonymous_Returns401()
    {
        Guid issueId = Guid.NewGuid();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(StateUrl(issueId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssueHardDeleted_CascadesAuthorQuestions()
    {
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage ask = await AppHttpClient.PostAsJsonAsync(AskUrl(issueId), new { message = "вопрос" });
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);

        await InvokeMessageAndWaitAsync(new IssueHardDeleted(issueId));

        int rows = await ExecuteInDb(db => db.IssueAuthorQuestions.CountAsync(x => x.IssueId == issueId));
        Assert.Equal(0, rows);
    }
}
