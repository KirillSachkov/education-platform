using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.SelfAssessment;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     Мягкая самооценка «Не уверен» (#691 t8): студент жмёт «Не уверен» на вопросе своей сессии →
///     <c>QuestionStudyState.Status = REVIEW</c> (НЕ WRONG), mastery НЕ двигается, вопрос всплывает в
///     «Моих ошибках» / SRS. Own-data (чужой item → 404), идемпотентно, валидация вердикта.
/// </summary>
public sealed class SelfAssessTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task SelfAssess_marks_question_REVIEW_and_creates_study_state_without_touching_mastery()
    {
        (Guid topicId, SessionDto session, var q) = await StartDrillAsync();
        SessionItemDto item = ItemFor(session, q.SingleQuestionId);

        HttpResponseMessage response = await SelfAssessAsync(session.Id, item.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SelfAssessmentDto dto = await ReadResultAsync<SelfAssessmentDto>(response);
        Assert.Equal(item.Id, dto.ItemId);
        Assert.Equal("REVIEW", dto.Status);

        // Row was created (none existed) and parked in REVIEW with a near-term SRS due — NOT marked wrong.
        QuestionStudyState? state = await GetStudyStateAsync(q.SingleQuestionId);
        Assert.NotNull(state);
        Assert.Equal(StudyStatus.REVIEW, state.Status);
        Assert.Equal(0, state.TimesWrong);
        Assert.NotNull(state.NextDueAt);

        // Mastery is a graded-answer signal — «Не уверен» is not an answer, so no mastery row is written.
        Assert.Null(await GetMasteryPercentAsync(topicId));
    }

    [Fact]
    public async Task SelfAssess_does_not_move_topic_mastery_baseline()
    {
        (Guid topicId, SessionDto session, var q) = await StartDrillAsync("PER_QUESTION");

        // Establish a mastery baseline by grading ONE question correct (→ 100% for the topic).
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);
        HttpResponseMessage grade = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{single.Id}/check",
            new CheckAnswerRequest([q.SingleCorrectOption], null));
        Assert.Equal(HttpStatusCode.OK, grade.StatusCode);
        Assert.Equal(100, await GetMasteryPercentAsync(topicId));

        // «Не уверен» on a DIFFERENT question must NOT pull mastery down as a wrong answer.
        SessionItemDto multi = ItemFor(session, q.MultiQuestionId);
        await SelfAssessAsync(session.Id, multi.Id);

        Assert.Equal(100, await GetMasteryPercentAsync(topicId));
        Assert.Equal(1, await GetMasteryAnswersCountAsync(topicId)); // still one graded question
    }

    [Fact]
    public async Task SelfAssessed_question_appears_in_mistakes_as_REVIEW()
    {
        (Guid topicId, SessionDto session, var q) = await StartDrillAsync();
        SessionItemDto item = ItemFor(session, q.SingleQuestionId);

        await SelfAssessAsync(session.Id, item.Id);

        HttpResponseMessage mistakesResponse = await Client.GetAsync("/trainer/mistakes");
        IReadOnlyList<MistakeItemDto> mistakes = await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(mistakesResponse);
        MistakeItemDto mistake = Assert.Single(mistakes);
        Assert.Equal(q.SingleQuestionId, mistake.QuestionId);
        Assert.Equal(topicId, mistake.TopicId);
        Assert.Equal("REVIEW", mistake.Status);
    }

    [Fact]
    public async Task SelfAssessed_question_surfaces_in_srs_due_once_due()
    {
        (Guid _, SessionDto session, var q) = await StartDrillAsync();
        SessionItemDto item = ItemFor(session, q.SingleQuestionId);

        await SelfAssessAsync(session.Id, item.Id);

        // SM-2 schedules at least a day out, so it's not due immediately — force next_due_at into the past.
        await SetNextDueInPastAsync(CurrentUserId, q.SingleQuestionId);

        HttpResponseMessage dueResponse = await Client.GetAsync("/trainer/srs/due");
        IReadOnlyList<SrsDueItemDto> due = await ReadResultAsync<IReadOnlyList<SrsDueItemDto>>(dueResponse);
        SrsDueItemDto dueItem = Assert.Single(due);
        Assert.Equal(q.SingleQuestionId, dueItem.QuestionId);
    }

    [Fact]
    public async Task SelfAssess_is_idempotent_stays_REVIEW()
    {
        (Guid _, SessionDto session, var q) = await StartDrillAsync();
        SessionItemDto item = ItemFor(session, q.SingleQuestionId);

        HttpResponseMessage first = await SelfAssessAsync(session.Id, item.Id);
        HttpResponseMessage second = await SelfAssessAsync(session.Id, item.Id);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("REVIEW", (await ReadResultAsync<SelfAssessmentDto>(second)).Status);

        // Still exactly one mistake row, still REVIEW (no duplicate, no flip to WRONG).
        HttpResponseMessage mistakesResponse = await Client.GetAsync("/trainer/mistakes");
        IReadOnlyList<MistakeItemDto> mistakes = await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(mistakesResponse);
        MistakeItemDto only = Assert.Single(mistakes);
        Assert.Equal("REVIEW", only.Status);
    }

    [Fact]
    public async Task SelfAssess_foreign_session_returns_404()
    {
        (Guid _, SessionDto session, var q) = await StartDrillAsync();
        SessionItemDto item = ItemFor(session, q.SingleQuestionId);

        // A different user cannot self-assess someone else's session item → indistinguishable from missing.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await SelfAssessAsync(session.Id, item.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SelfAssess_unknown_item_returns_404()
    {
        (Guid _, SessionDto session, _) = await StartDrillAsync();

        HttpResponseMessage response = await SelfAssessAsync(session.Id, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SelfAssess_unknown_verdict_returns_400()
    {
        (Guid _, SessionDto session, var q) = await StartDrillAsync();
        SessionItemDto item = ItemFor(session, q.SingleQuestionId);

        HttpResponseMessage response = await SelfAssessAsync(session.Id, item.Id, "MAYBE");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.self_assess.invalid", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task SelfAssess_is_case_sensitive_lowercase_verdict_returns_400()
    {
        (Guid _, SessionDto session, var q) = await StartDrillAsync();
        SessionItemDto item = ItemFor(session, q.SingleQuestionId);

        HttpResponseMessage response = await SelfAssessAsync(session.Id, item.Id, "unsure");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SelfAssess_on_answered_item_returns_409_already_answered()
    {
        (Guid _, SessionDto session, var q) = await StartDrillAsync("PER_QUESTION");
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);

        // Answer the item first → it now carries a verdict. «Не уверен» is a PRE-answer action.
        HttpResponseMessage grade = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{single.Id}/check",
            new CheckAnswerRequest([q.SingleCorrectOption], null));
        Assert.Equal(HttpStatusCode.OK, grade.StatusCode);

        HttpResponseMessage response = await SelfAssessAsync(session.Id, single.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("trainer.self_assess.already.answered", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task SelfAssess_anonymous_returns_401()
    {
        (Guid _, SessionDto session, var q) = await StartDrillAsync();
        SessionItemDto item = ItemFor(session, q.SingleQuestionId);

        RemoveAuthentication();
        HttpResponseMessage response = await SelfAssessAsync(session.Id, item.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    private static SessionItemDto ItemFor(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId);

    private Task<HttpResponseMessage> SelfAssessAsync(Guid sessionId, Guid itemId, string verdict = "UNSURE") =>
        Client.PatchAsJsonAsync(
            $"/trainer/sessions/{sessionId}/items/{itemId}/self-assess",
            new SelfAssessRequest(verdict));

    private Task<QuestionStudyState?> GetStudyStateAsync(Guid questionId) =>
        ExecuteInDbAsync(db =>
            db.QuestionStudyStates
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == CurrentUserId && s.QuestionId == questionId));

    private Task<int?> GetMasteryPercentAsync(Guid topicId) =>
        ExecuteInDbAsync(db =>
            db.TopicMasteries
                .Where(m => m.UserId == CurrentUserId && m.TopicId == topicId)
                .Select(m => (int?)m.MasteryPercent)
                .SingleOrDefaultAsync());

    private Task<int?> GetMasteryAnswersCountAsync(Guid topicId) =>
        ExecuteInDbAsync(db =>
            db.TopicMasteries
                .Where(m => m.UserId == CurrentUserId && m.TopicId == topicId)
                .Select(m => (int?)m.AnswersCount)
                .SingleOrDefaultAsync());

    private Task SetNextDueInPastAsync(Guid userId, Guid questionId) =>
        ExecuteInDbAsync(async db =>
            await db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.question_study_states SET next_due_at = {DateTimeOffset.UtcNow.AddDays(-1)} WHERE user_id = {userId} AND question_id = {questionId}"));

    private async Task<(Guid TopicId, SessionDto Session, TrainerQuestionFixtures.SeededQuestions Questions)> StartDrillAsync(
        string? revealPolicy = null)
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, QuestionCount: null, RevealPolicy: revealPolicy));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        return (topicId, session, questions);
    }

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "self-assess-topic", "Тема самооценки", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return (topicId, questions);
    }
}
