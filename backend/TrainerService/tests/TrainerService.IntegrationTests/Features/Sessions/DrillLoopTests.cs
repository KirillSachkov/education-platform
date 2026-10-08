using System.Net;
using System.Net.Http.Json;
using Core.Database;
using Microsoft.Extensions.DependencyInjection;
using TrainerService.Contracts.Progress;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     End-to-end DRILL loop: start → snapshot questions (no grading key leaks) → instant
///     per-answer check (CORRECT/INCORRECT/EXACT/OPEN) → mastery EWMA → complete → progress.
/// </summary>
public sealed class DrillLoopTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task StartSession_snapshots_questions_without_correct_answers_or_grading_key()
    {
        (Guid _, SessionDto session, HttpResponseMessage startResponse, _) = await StartDrillAsync();

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Equal("DRILL", session.Mode);
        Assert.Equal("IN_PROGRESS", session.Status);
        Assert.Equal(4, session.Items.Count); // four canned questions
        Assert.Null(session.ScorePercent);

        // No-leak invariant: every unanswered item must expose options WITHOUT a correct flag,
        // and must NOT carry correctOptionIds / referenceAnswer / explanation / score. The
        // verdict is PENDING (= not yet graded — that's a status, not an answer leak).
        foreach (SessionItemDto item in session.Items)
        {
            Assert.False(item.IsAnswered);
            Assert.Null(item.CorrectOptionIds);
            Assert.Null(item.ReferenceAnswer);
            Assert.Null(item.Explanation);
            Assert.Null(item.AnswerRaw);
            Assert.Null(item.ScorePercent);
            Assert.Equal("PENDING", item.Verdict);
            Assert.NotNull(item.QuestionText); // present (not redacted) for this PRO drill
        }

        // Strongest guarantee — the raw JSON body for an unanswered session must not contain any
        // actual secret VALUE: not the grading-key blob, not the per-question Explanation, and not
        // the EXACT/OPEN reference texts (those are NOT shown as selectable options, so their
        // presence would be a true leak). Choice option-id GUIDs legitimately appear as selectable
        // options without a correct-flag — that's the whole point of the shuffle, not a leak.
        string raw = await ReadRawAsync(startResponse);
        Assert.DoesNotContain("gradingKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrainerQuestionFixtures.ExactReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.SingleExplanation, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.MultiExplanation, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAnswer_correct_single_choice_is_CORRECT_100_and_raises_mastery()
    {
        (Guid topicId, SessionDto session, _, var q) = await StartDrillAsync();
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);

        CheckAnswerResponse result = await CheckAsync(
            session.Id, single.Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));

        Assert.Equal("CORRECT", result.Verdict);
        Assert.Equal(100, result.ScorePercent);
        // Review fields revealed only AFTER answering.
        Assert.Equal([q.SingleCorrectOption], result.CorrectOptionIds);
        Assert.NotNull(result.Explanation);

        // Mastery rises off the floor — first answer = the score (100).
        TopicMasteryDto mastery = await GetMasteryAsync(topicId);
        Assert.Equal(100, mastery.MasteryPercent);
        Assert.Equal(1, mastery.AnswersCount);
        Assert.False(mastery.IsWeak);
    }

    [Fact]
    public async Task CheckAnswer_drill_feeds_study_state_known_and_mistakes()
    {
        // DRILL («тест») теперь тоже питает study-state (как LEARN): верный ответ → KNOWN (двигает
        // «изучено»), неверный → WRONG → «Мои ошибки». На старом коде study-state DRILL не трогал,
        // и /trainer/mistakes был бы пуст — этот тест разделяет старое и новое поведение.
        (Guid topicId, SessionDto session, _, var q) = await StartDrillAsync();

        // Верный single → KNOWN (в «ошибки» не попадает).
        await CheckAsync(
            session.Id, ItemFor(session, q.SingleQuestionId).Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));

        // Неверный multi → WRONG → «Мои ошибки».
        await CheckAsync(
            session.Id, ItemFor(session, q.MultiQuestionId).Id,
            new CheckAnswerRequest([q.MultiWrongOption], null));

        HttpResponseMessage mistakesResponse = await Client.GetAsync("/trainer/mistakes");
        IReadOnlyList<MistakeItemDto> mistakes =
            await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(mistakesResponse);
        MistakeItemDto mistake = Assert.Single(mistakes); // только неверный multi, не верный single
        Assert.Equal(q.MultiQuestionId, mistake.QuestionId);
        Assert.Equal(topicId, mistake.TopicId);
    }

    [Fact]
    public async Task CheckAnswer_wrong_multi_choice_is_INCORRECT_0()
    {
        (Guid topicId, SessionDto session, _, var q) = await StartDrillAsync();
        SessionItemDto multi = ItemFor(session, q.MultiQuestionId);

        // Only one of the two correct options + a wrong one → not a set-equal match → INCORRECT.
        CheckAnswerResponse result = await CheckAsync(
            session.Id, multi.Id,
            new CheckAnswerRequest([q.MultiCorrectOptions[0], q.MultiWrongOption], null));

        Assert.Equal("INCORRECT", result.Verdict);
        Assert.Equal(0, result.ScorePercent);

        TopicMasteryDto mastery = await GetMasteryAsync(topicId);
        Assert.Equal(0, mastery.MasteryPercent);
        Assert.Equal(1, mastery.AnswersCount);
    }

    [Fact]
    public async Task CheckAnswer_exact_text_normalized_match_is_CORRECT()
    {
        (Guid _, SessionDto session, _, var q) = await StartDrillAsync();
        SessionItemDto exact = ItemFor(session, q.ExactQuestionId);

        // Reference is "Garbage Collector" — normalization lowercases + drops non-alnum,
        // so "  garbage-collector! " must match.
        CheckAnswerResponse result = await CheckAsync(
            session.Id, exact.Id,
            new CheckAnswerRequest(null, "  garbage-collector! "));

        Assert.Equal("CORRECT", result.Verdict);
        Assert.Equal(100, result.ScorePercent);
        Assert.Equal(TrainerQuestionFixtures.ExactReference, result.ReferenceAnswer);
    }

    [Fact]
    public async Task CheckAnswer_open_text_is_AI_graded_inline_with_reference_and_mastery_change()
    {
        // DRILL is non-MOCK → OPEN_TEXT is now AI-graded inline (#568 W2). The (faked) grader scores
        // any non-empty answer CORRECT/100, so the verdict/score are revealed (PER_QUESTION) and the
        // topic mastery is updated like an auto-graded answer.
        (Guid topicId, SessionDto session, _, var q) = await StartDrillAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        CheckAnswerResponse result = await CheckAsync(
            session.Id, open.Id,
            new CheckAnswerRequest(null, "Мой свободный ответ про поколения."));

        Assert.Equal("CORRECT", result.Verdict);
        Assert.Equal(100, result.ScorePercent);
        Assert.Equal(TrainerQuestionFixtures.OpenReference, result.ReferenceAnswer);

        // Inline-graded open answer updates mastery (first answer = score = 100).
        TopicMasteryDto mastery = await GetMasteryAsync(topicId);
        Assert.Equal(100, mastery.MasteryPercent);
        Assert.Equal(1, mastery.AnswersCount);
    }

    [Fact]
    public async Task CheckAnswer_on_already_answered_item_returns_409()
    {
        (Guid _, SessionDto session, _, var q) = await StartDrillAsync();
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);

        CheckAnswerResponse first = await CheckAsync(
            session.Id, single.Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));
        Assert.Equal("CORRECT", first.Verdict);

        // Second check of the same item → 409 conflict.
        HttpResponseMessage second = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{single.Id}/check",
            new CheckAnswerRequest([q.SingleWrongOption], null));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("trainer.session.answer.already.checked", await ReadErrorCodeAsync(second));
    }

    [Fact]
    public async Task Concurrent_session_updates_reject_the_stale_writer()
    {
        (_, SessionDto session, _, var q) = await StartDrillAsync();
        Guid itemId = ItemFor(session, q.SingleQuestionId).Id;

        using IServiceScope firstScope = Factory.Services.CreateScope();
        using IServiceScope secondScope = Factory.Services.CreateScope();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<ITrainingSessionsRepository>();
        var secondRepository = secondScope.ServiceProvider.GetRequiredService<ITrainingSessionsRepository>();
        var firstTransaction = firstScope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var secondTransaction = secondScope.ServiceProvider.GetRequiredService<ITransactionManager>();

        TrainingSession first = (await firstRepository.GetWithItemsAsync(s => s.Id == session.Id)).Value;
        TrainingSession stale = (await secondRepository.GetWithItemsAsync(s => s.Id == session.Id)).Value;

        Assert.True(first.RecordAnswer(itemId, "first", 100, AnswerVerdict.CORRECT, null).IsSuccess);
        Assert.True(stale.RecordAnswer(itemId, "stale", 0, AnswerVerdict.INCORRECT, null).IsSuccess);

        Assert.True((await firstTransaction.SaveChangesAsync()).IsSuccess);
        UnitResult<Error> staleSave = await secondTransaction.SaveChangesAsync();

        Assert.True(staleSave.IsFailure);
        Assert.Equal("concurrency.conflict", staleSave.Error.Messages[0].Code);
    }

    [Fact]
    public async Task CompleteSession_sets_COMPLETED_with_avg_score_and_progress_shows_mastery()
    {
        (Guid topicId, SessionDto session, _, var q) = await StartDrillAsync();

        // Answer two of the three auto-graded questions: single correct (100), multi wrong (0).
        // The third auto-graded (exact) is left unanswered → counts as 0 in the denominator.
        await CheckAsync(
            session.Id, ItemFor(session, q.SingleQuestionId).Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));
        await CheckAsync(
            session.Id, ItemFor(session, q.MultiQuestionId).Id,
            new CheckAnswerRequest([q.MultiWrongOption], null));

        HttpResponseMessage completeResponse =
            await Client.PostAsync($"/trainer/sessions/{session.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        SessionSummaryDto summary = await ReadResultAsync<SessionSummaryDto>(completeResponse);

        Assert.Equal("COMPLETED", summary.Status);
        Assert.Equal(33, summary.ScorePercent); // 1 correct of 3 auto-graded (single 100, multi 0, exact unanswered 0)
        Assert.Equal(4, summary.TotalItems);
        Assert.Equal(2, summary.AnsweredItems);
        Assert.Equal(1, summary.CorrectItems);
        Assert.NotNull(summary.CompletedAt);

        // GET /trainer/progress reflects the mastery + the recent session.
        HttpResponseMessage progressResponse = await Client.GetAsync("/trainer/progress");
        TrainerProgressDto progress = await ReadResultAsync<TrainerProgressDto>(progressResponse);

        TopicMasteryDto mastery = Assert.Single(progress.Mastery, m => m.TopicId == topicId);
        // Derived (#691): difficulty-weighted avg of the latest score per unique question — single is
        // JUNIOR (weight 0.7, score 100), multi is MIDDLE (weight 1.0, score 0):
        // round((0.7*100 + 1.0*0) / (0.7 + 1.0)) = round(70 / 1.7) = 41. Two distinct questions answered.
        Assert.Equal(41, mastery.MasteryPercent);
        Assert.Equal(2, mastery.AnswersCount);

        RecentSessionDto recent = Assert.Single(progress.RecentSessions, s => s.Id == session.Id);
        Assert.Equal("COMPLETED", recent.Status);
        Assert.Equal(33, recent.ScorePercent);
    }

    [Fact]
    public async Task GetSession_does_not_leak_grading_key_for_unanswered_items()
    {
        (Guid _, SessionDto session, _, var q) = await StartDrillAsync();
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);

        await CheckAsync(
            session.Id, single.Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));

        HttpResponseMessage getResponse = await Client.GetAsync($"/trainer/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        SessionDto fetched = await ReadResultAsync<SessionDto>(getResponse);

        SessionItemDto answered = ItemFor(fetched, q.SingleQuestionId);
        Assert.True(answered.IsAnswered);
        Assert.Equal([q.SingleCorrectOption], answered.CorrectOptionIds);

        // Every item that has NOT been answered must still hide its correct answers.
        foreach (SessionItemDto item in fetched.Items.Where(i => !i.IsAnswered))
        {
            Assert.Null(item.CorrectOptionIds);
            Assert.Null(item.ReferenceAnswer);
            Assert.Null(item.Explanation);
        }

        // The OPEN/EXACT reference text of unanswered items must not appear in the raw body.
        string raw = await ReadRawAsync(getResponse);
        Assert.DoesNotContain(TrainerQuestionFixtures.ExactReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSession_of_other_user_returns_404()
    {
        (Guid _, SessionDto session, _, _) = await StartDrillAsync();

        // Switch to a different user — the session is scoped by UserId.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await Client.GetAsync($"/trainer/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartSession_with_non_drill_mode_is_rejected()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("MOCK", topicId, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.session.invalid.mode", await ReadErrorCodeAsync(response));
    }

    // --- helpers ---

    private static SessionItemDto ItemFor(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId);

    private async Task<CheckAnswerResponse> CheckAsync(Guid sessionId, Guid itemId, CheckAnswerRequest request)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{sessionId}/answers/{itemId}/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<CheckAnswerResponse>(response);
    }

    private async Task<TopicMasteryDto> GetMasteryAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.GetAsync("/trainer/progress");
        TrainerProgressDto dto = await ReadResultAsync<TrainerProgressDto>(response);
        return Assert.Single(dto.Mastery, m => m.TopicId == topicId);
    }

    /// <summary>Seeds a published topic with a FREE bank + the canonical 4 local questions. Leaves the
    /// client authenticated as a regular participant.</summary>
    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "drill-topic", "Тема для дрилла", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        AuthenticateAs("platform-participant");
        return (topicId, questions);
    }

    private async Task<(Guid TopicId, SessionDto Session, HttpResponseMessage Response, TrainerQuestionFixtures.SeededQuestions Questions)> StartDrillAsync()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) = await SeedPublishedFreeTopicAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        return (topicId, session, response, questions);
    }
}
