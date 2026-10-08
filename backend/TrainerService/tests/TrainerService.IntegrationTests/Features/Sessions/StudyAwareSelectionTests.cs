using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.Questions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     Study-state-aware question selection (#691 t2): session starts (DRILL / LEARN / MOCK) no longer
///     repeat the same questions. Selection priority is NEW/WRONG/REVIEW → SEEN → KNOWN (tail);
///     recently-seen questions are excluded while fresh candidates can still fill N; if the pool after
///     exclusions is smaller than N it falls back to least-recently-seen; freemium pool stays respected.
/// </summary>
public sealed class StudyAwareSelectionTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    // The production window is 6h — these tests backdate "old" study-states well past it and keep
    // "recent" ones inside it.
    private static readonly TimeSpan WellPastWindow = TimeSpan.FromDays(2);

    // --- DRILL: priority ---

    [Fact]
    public async Task Drill_picks_new_over_known_when_capacity_is_limited()
    {
        // User KNOWS 8/10 (study-states backdated → fresh, so this isolates PRIORITY from recency).
        // The two questions without a study-state are NEW. A 2-question session must pick the 2 NEW
        // ones — KNOWN must not surface until the fresh pool is exhausted.
        (Guid _, Guid topicId, List<Guid> ids) = await SeedPublishedStudyTopicAsync(questionCount: 10);
        List<Guid> known = ids.Take(8).ToList();
        List<Guid> @new = ids.Skip(8).ToList();
        foreach (Guid id in known)
            await MarkKnownAsync(topicId, id, ageBack: WellPastWindow);

        SessionDto session = await StartDrillAsync(topicId, questionCount: 2);

        Assert.Equal(2, session.Items.Count);
        Assert.Equal(@new.OrderBy(g => g), session.Items.Select(i => i.QuestionId).OrderBy(g => g));
        Assert.DoesNotContain(session.Items, i => known.Contains(i.QuestionId));
    }

    [Fact]
    public async Task Drill_orders_new_first_then_known_to_the_tail_when_all_fit()
    {
        // Same 8-known / 2-new setup but request the whole pool (N=10): every question appears, but the
        // 2 NEW must occupy the head (lowest SortIndex) and the 8 KNOWN the tail.
        (Guid _, Guid topicId, List<Guid> ids) = await SeedPublishedStudyTopicAsync(questionCount: 10);
        List<Guid> known = ids.Take(8).ToList();
        List<Guid> @new = ids.Skip(8).ToList();
        foreach (Guid id in known)
            await MarkKnownAsync(topicId, id, ageBack: WellPastWindow);

        SessionDto session = await StartDrillAsync(topicId, questionCount: 10);

        Assert.Equal(10, session.Items.Count);
        List<Guid> ordered = session.Items.OrderBy(i => i.SortIndex).Select(i => i.QuestionId).ToList();
        Assert.Equal(@new.OrderBy(g => g), ordered.Take(2).OrderBy(g => g));   // head = the 2 NEW
        Assert.Equal(known.OrderBy(g => g), ordered.Skip(2).OrderBy(g => g));  // tail = the 8 KNOWN
    }

    // --- DRILL: recency vs priority ---

    [Fact]
    public async Task Drill_excludes_recently_seen_even_over_higher_priority_while_fresh_available()
    {
        // 2 WRONG questions seen JUST NOW (highest priority, but recently-seen) + 2 KNOWN questions seen
        // long ago (lowest priority, but fresh). A 2-question session must pick the fresh KNOWN ones:
        // the "exclude recently-seen while fresh candidates remain" rule outranks the priority bucket.
        (Guid _, Guid topicId, List<Guid> ids) = await SeedPublishedStudyTopicAsync(questionCount: 4);
        List<Guid> recentWrong = ids.Take(2).ToList();
        List<Guid> freshKnown = ids.Skip(2).ToList();
        foreach (Guid id in recentWrong)
            await MarkWrongAsync(topicId, id, ageBack: TimeSpan.Zero); // recently seen
        foreach (Guid id in freshKnown)
            await MarkKnownAsync(topicId, id, ageBack: WellPastWindow); // fresh

        SessionDto session = await StartDrillAsync(topicId, questionCount: 2);

        Assert.Equal(2, session.Items.Count);
        Assert.Equal(freshKnown.OrderBy(g => g), session.Items.Select(i => i.QuestionId).OrderBy(g => g));
        Assert.DoesNotContain(session.Items, i => recentWrong.Contains(i.QuestionId));
    }

    [Fact]
    public async Task Drill_falls_back_to_least_recently_seen_when_pool_smaller_than_N()
    {
        // ALL 4 questions are recently-seen (inside the window) at distinct times → no fresh candidates.
        // A 2-question session must fall back to the two LEAST-recently-seen (oldest LastSeenAt).
        (Guid _, Guid topicId, List<Guid> ids) = await SeedPublishedStudyTopicAsync(questionCount: 4);
        await MarkKnownAsync(topicId, ids[0], ageBack: TimeSpan.FromHours(5)); // oldest (still < 6h window)
        await MarkKnownAsync(topicId, ids[1], ageBack: TimeSpan.FromHours(4));
        await MarkKnownAsync(topicId, ids[2], ageBack: TimeSpan.FromHours(2));
        await MarkKnownAsync(topicId, ids[3], ageBack: TimeSpan.FromHours(1)); // newest

        SessionDto session = await StartDrillAsync(topicId, questionCount: 2);

        Assert.Equal(2, session.Items.Count);
        Assert.Equal(
            new[] { ids[0], ids[1] }.OrderBy(g => g),
            session.Items.Select(i => i.QuestionId).OrderBy(g => g));
    }

    // --- DRILL: freemium pool still respected ---

    [Fact]
    public async Task Drill_non_pro_selects_only_free_samples_and_still_orders_by_study_state()
    {
        // Non-PRO: only the 2 free samples of the canonical bank (JUNIOR single + MIDDLE multi) are
        // eligible — the EXACT (non-free) and OPEN (never free) questions must never enter the pool.
        // On top of that the free NEW question must come before the free KNOWN one.
        EntitlementChecker.DenyAll();
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions q) = await SeedCanonicalFreeTopicAsync();

        // Make the free single-choice KNOWN (fresh); the free multi-choice stays NEW.
        await MarkKnownAsync(topicId, q.SingleQuestionId, ageBack: WellPastWindow);

        SessionDto session = await StartDrillAsync(topicId, questionCount: 10); // capped to the free pool (2)

        Assert.Equal(2, session.Items.Count);
        Assert.Equal(
            new[] { q.SingleQuestionId, q.MultiQuestionId }.OrderBy(g => g),
            session.Items.Select(i => i.QuestionId).OrderBy(g => g));
        Assert.DoesNotContain(session.Items, i => i.QuestionId == q.ExactQuestionId);
        Assert.DoesNotContain(session.Items, i => i.QuestionId == q.OpenQuestionId);

        // NEW (multi) ahead of KNOWN (single) inside the free pool.
        List<Guid> ordered = session.Items.OrderBy(i => i.SortIndex).Select(i => i.QuestionId).ToList();
        Assert.Equal(q.MultiQuestionId, ordered[0]);
        Assert.Equal(q.SingleQuestionId, ordered[1]);
    }

    // --- LEARN ---

    [Fact]
    public async Task Learn_picks_new_over_known_when_capacity_is_limited()
    {
        (Guid _, Guid topicId, List<Guid> ids) = await SeedPublishedStudyTopicAsync(questionCount: 10);
        List<Guid> known = ids.Take(8).ToList();
        List<Guid> @new = ids.Skip(8).ToList();
        foreach (Guid id in known)
            await MarkKnownAsync(topicId, id, ageBack: WellPastWindow);

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/learn-sessions",
            new StartLearnSessionRequest(topicId, QuestionCount: 2));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);

        Assert.Equal("LEARN", session.Mode);
        Assert.Equal(2, session.Items.Count);
        Assert.Equal(@new.OrderBy(g => g), session.Items.Select(i => i.QuestionId).OrderBy(g => g));
        Assert.DoesNotContain(session.Items, i => known.Contains(i.QuestionId));
    }

    // --- MOCK ---

    [Fact]
    public async Task Mock_picks_new_over_known_when_capacity_is_limited()
    {
        // MOCK is PRO-only; default EntitlementChecker = GrantAll → PRO. Pool is the track's published
        // topics' banks; same priority rule applies cross-topic.
        (Guid trackId, Guid topicId, List<Guid> ids) = await SeedPublishedStudyTopicAsync(questionCount: 10);
        List<Guid> known = ids.Take(8).ToList();
        List<Guid> @new = ids.Skip(8).ToList();
        foreach (Guid id in known)
            await MarkKnownAsync(topicId, id, ageBack: WellPastWindow);

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(trackId, QuestionCount: 2, TimeLimitSeconds: null, Difficulty: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);

        Assert.Equal("MOCK", session.Mode);
        Assert.Equal(2, session.Items.Count);
        Assert.Equal(@new.OrderBy(g => g), session.Items.Select(i => i.QuestionId).OrderBy(g => g));
        Assert.DoesNotContain(session.Items, i => known.Contains(i.QuestionId));
    }

    // --- helpers ---

    private async Task<SessionDto> StartDrillAsync(Guid topicId, int questionCount)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, questionCount));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    /// <summary>
    ///     Seeds a published topic (STUDY bank) with <paramref name="questionCount"/> SINGLE_CHOICE
    ///     questions, then authenticates the client as a regular participant (same DefaultUserId, so
    ///     study-states written for the caller line up with the session start). Returns track + topic +
    ///     the question ids in seed order.
    /// </summary>
    private async Task<(Guid TrackId, Guid TopicId, List<Guid> QuestionIds)> SeedPublishedStudyTopicAsync(int questionCount)
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "sel-topic", "Тема выбора", "Runtime", null, "BACKEND", null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;

        var ids = new List<Guid>(questionCount);
        for (int i = 0; i < questionCount; i++)
        {
            TrainerQuestion question = await TrainerQuestionFixtures.SeedSingleChoiceAsync(
                Factory, bankId, $"Вопрос {i}", QuestionDifficulty.MIDDLE, "memory");
            ids.Add(question.Id);
        }

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        AuthenticateAs("platform-participant");
        return (trackId, topicId, ids);
    }

    /// <summary>Seeds a published topic with the canonical 4-question FREE bank. Leaves the client as participant.</summary>
    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedCanonicalFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "canon-topic", "Канон", "Runtime", null, "BACKEND", null, null));
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

    private Task MarkKnownAsync(Guid topicId, Guid questionId, TimeSpan ageBack) =>
        MarkStudyStateAsync(topicId, questionId, correct: true, ageBack);

    private Task MarkWrongAsync(Guid topicId, Guid questionId, TimeSpan ageBack) =>
        MarkStudyStateAsync(topicId, questionId, correct: false, ageBack);

    /// <summary>
    ///     Inserts a <see cref="QuestionStudyState"/> for the current caller via the domain (KNOWN when
    ///     <paramref name="correct"/>, else WRONG), then backdates <c>last_seen_at</c> by
    ///     <paramref name="ageBack"/> (raw SQL — the domain only stamps "now"). <c>TimeSpan.Zero</c> keeps
    ///     it "recently seen"; a value past the 6h window makes it "fresh".
    /// </summary>
    private async Task MarkStudyStateAsync(Guid topicId, Guid questionId, bool correct, TimeSpan ageBack)
    {
        await ExecuteInDbAsync(async db =>
        {
            QuestionStudyState state = QuestionStudyState.Create(CurrentUserId, questionId, topicId);
            state.RecordTestResult(correct, DateTimeOffset.UtcNow);
            db.QuestionStudyStates.Add(state);
            await db.SaveChangesAsync();
            return 0;
        });

        if (ageBack > TimeSpan.Zero)
        {
            DateTimeOffset backdated = DateTimeOffset.UtcNow - ageBack;
            await ExecuteInDbAsync(async db =>
            {
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE trainer.question_study_states SET last_seen_at = {0} WHERE user_id = {1} AND question_id = {2}",
                    backdated, CurrentUserId, questionId);
                return 0;
            });
        }
    }
}
