using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.TopicBanks;
using TrainerService.Infrastructure.Postgres;
using TrainerService.IntegrationTests.Infrastructure;
using TrainerService.Web.Configuration;

namespace TrainerService.IntegrationTests.Features.Questions;

/// <summary>
///     Per-question free-sample access + server-side redaction (#674): the question-mutation handlers
///     recompute IsFreeSample, non-PRO callers only ever see free-sample content (stems redacted on
///     locked items across question-list / SRS / mistakes), non-PRO session composition caps to the
///     free pool, and the recompute-free-samples backfill restores flags.
/// </summary>
public sealed class FreeSampleAccessTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    // --- recompute on mutation (through the real admin handlers, not the fixture) ---

    [Fact]
    public async Task CreateQuestion_recomputes_free_samples_lowest_sortkey_first()
    {
        (Guid topicId, Guid bankId) = await SeedPublishedTopicAndBankAsync();

        // Three JUNIOR closed questions appended in order → q1 has the lowest SortKey.
        Guid q1 = await CreateClosedAsync(bankId, "первый");
        await CreateClosedAsync(bankId, "второй");
        await CreateClosedAsync(bankId, "третий");

        // 3 eligible @ 10% → ceil(0.3) = 1 free → only q1 (lowest SortKey).
        QuestionListDto list = await GetListAsFreeParticipantAsync(topicId);
        Assert.Equal(1, list.Items.Count(i => !i.IsLocked));
        QuestionListItemDto free = Assert.Single(list.Items, i => !i.IsLocked);
        Assert.Equal(q1, free.QuestionId);
        Assert.NotNull(free.Stem);
        Assert.All(list.Items.Where(i => i.IsLocked), i => Assert.Null(i.Stem)); // redacted
    }

    [Fact]
    public async Task DeleteQuestion_recomputes_free_samples_promoting_next()
    {
        (Guid topicId, Guid bankId) = await SeedPublishedTopicAndBankAsync();
        Guid q1 = await CreateClosedAsync(bankId, "первый");
        Guid q2 = await CreateClosedAsync(bankId, "второй");
        await CreateClosedAsync(bankId, "третий");

        // Delete the currently-free q1 → recompute promotes q2 (next lowest SortKey).
        HttpResponseMessage delete = await Client.DeleteAsync($"/trainer/questions/{q1}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        QuestionListDto list = await GetListAsFreeParticipantAsync(topicId);
        Assert.Equal(2, list.Items.Count);
        QuestionListItemDto free = Assert.Single(list.Items, i => !i.IsLocked);
        Assert.Equal(q2, free.QuestionId);
    }

    [Fact]
    public async Task UpdateQuestion_changing_difficulty_recomputes_free_samples()
    {
        (Guid topicId, Guid bankId) = await SeedPublishedTopicAndBankAsync();
        Guid q1 = await CreateClosedAsync(bankId, "первый");   // JUNIOR, free
        Guid q2 = await CreateClosedAsync(bankId, "второй");   // JUNIOR, locked

        // Move q2 to its own SENIOR bucket → min-1 per bucket makes it free too.
        HttpResponseMessage update = await Client.PutAsJsonAsync(
            $"/trainer/questions/{q2}",
            new QuestionInputDto("второй", "SINGLE_CHOICE", null, null, "SENIOR", null,
                [new QuestionOptionInputDto("a", true), new QuestionOptionInputDto("b", false)]));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        QuestionListDto list = await GetListAsFreeParticipantAsync(topicId);
        Assert.All(list.Items, i => Assert.False(i.IsLocked)); // both buckets now have one free each
        Assert.Contains(list.Items, i => i.QuestionId == q1);
        Assert.Contains(list.Items, i => i.QuestionId == q2);
    }

    // --- session composition for non-PRO caps to the free pool ---

    [Fact]
    public async Task NonPro_session_caps_requested_count_to_free_pool()
    {
        (Guid topicId, _) = await SeedCanonicalFreeTopicAsync();

        AuthenticateAs("platform-participant");
        EntitlementChecker.DenyAll();
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, QuestionCount: 50));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        Assert.Equal(2, session.Items.Count); // capped to free pool (single + multi)
    }

    // --- redaction: mistakes ---

    [Fact]
    public async Task Mistakes_redacts_locked_question_stem_for_non_pro_but_keeps_free()
    {
        (Guid topicId, var q) = await SeedCanonicalFreeTopicAsync();
        Guid userId = Guid.NewGuid();
        await SeedWrongStudyStateAsync(userId, q.SingleQuestionId, topicId); // free sample
        await SeedWrongStudyStateAsync(userId, q.OpenQuestionId, topicId);   // OPEN_TEXT → never free

        AuthenticateAs("platform-participant", userId);
        EntitlementChecker.DenyAll();
        IReadOnlyList<MistakeItemDto> mistakes =
            await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(await Client.GetAsync("/trainer/mistakes"));

        MistakeItemDto free = Assert.Single(mistakes, m => m.QuestionId == q.SingleQuestionId);
        Assert.False(free.IsLocked);
        Assert.NotNull(free.Stem);

        MistakeItemDto locked = Assert.Single(mistakes, m => m.QuestionId == q.OpenQuestionId);
        Assert.True(locked.IsLocked);
        Assert.Equal("pro_required", locked.LockReason);
        Assert.Null(locked.Stem); // redacted
    }

    [Fact]
    public async Task Mistakes_shows_full_content_for_pro()
    {
        (Guid topicId, var q) = await SeedCanonicalFreeTopicAsync();
        Guid userId = Guid.NewGuid();
        await SeedWrongStudyStateAsync(userId, q.OpenQuestionId, topicId);

        AuthenticateAs("platform-participant", userId);
        EntitlementChecker.GrantAll(); // PRO
        IReadOnlyList<MistakeItemDto> mistakes =
            await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(await Client.GetAsync("/trainer/mistakes"));

        MistakeItemDto open = Assert.Single(mistakes, m => m.QuestionId == q.OpenQuestionId);
        Assert.False(open.IsLocked);
        Assert.NotNull(open.Stem);
    }

    // --- recompute: STUDY-only allocation (code-review SF-1) ---

    [Fact]
    public async Task Recompute_frees_study_question_not_mock_even_when_mock_sorts_earlier()
    {
        // SF-1: free-sample allocation must consider STUDY banks only. A MOCK-bank question that sorts
        // EARLIER in the same difficulty bucket must NOT steal the single free slot from the STUDY
        // question (MOCK is hard PRO-gated → never a free sample).
        (Guid topicId, Guid studyBankId) = await SeedPublishedTopicAndBankAsync();

        (Guid mockQid, Guid studyQid) = await ExecuteInDbAsync(async db =>
        {
            TopicBank mock = TopicBank
                .Create(topicId, BankTier.PAID, null, SortKey.Initial().Value, BankPurpose.MOCK).Value;
            await db.TopicBanks.AddAsync(mock);

            TrainerQuestion mockQ = TrainerQuestion.Create(
                mock.Id, "MOCK junior", TrainerQuestionType.SINGLE_CHOICE, null, null,
                QuestionDifficulty.JUNIOR, null, SortKey.Initial().Value,
                [("a", true), ("b", false)]).Value;
            TrainerQuestion studyQ = TrainerQuestion.Create(
                studyBankId, "STUDY junior", TrainerQuestionType.SINGLE_CHOICE, null, null,
                QuestionDifficulty.JUNIOR, null, SortKey.After(SortKey.Initial()).Value,
                [("a", true), ("b", false)]).Value;
            await db.TrainerQuestions.AddAsync(mockQ);
            await db.TrainerQuestions.AddAsync(studyQ);
            await db.SaveChangesAsync();
            return (mockQ.Id, studyQ.Id);
        });

        using (IServiceScope scope = Factory.Services.CreateScope())
        {
            var recomputer = scope.ServiceProvider.GetRequiredService<TrainerFreeSampleRecomputer>();
            var db = scope.ServiceProvider.GetRequiredService<TrainerServiceDbContext>();
            await recomputer.RecomputeForTopicAsync(topicId);
            await db.SaveChangesAsync();
        }

        Assert.True(await IsFreeSampleAsync(studyQid));   // STUDY question got the single free slot
        Assert.False(await IsFreeSampleAsync(mockQid));   // MOCK question excluded from allocation
    }

    // --- redaction: SRS due ---

    [Fact]
    public async Task SrsDue_redacts_locked_question_stem_for_non_pro()
    {
        (Guid topicId, var q) = await SeedCanonicalFreeTopicAsync();
        Guid userId = Guid.NewGuid();
        await SeedWrongStudyStateAsync(userId, q.OpenQuestionId, topicId);
        await SetNextDueInPastAsync(userId, q.OpenQuestionId);

        AuthenticateAs("platform-participant", userId);
        EntitlementChecker.DenyAll();
        IReadOnlyList<SrsDueItemDto> due =
            await ReadResultAsync<IReadOnlyList<SrsDueItemDto>>(await Client.GetAsync("/trainer/srs/due"));

        SrsDueItemDto item = Assert.Single(due);
        Assert.True(item.IsLocked);
        Assert.Equal("pro_required", item.LockReason);
        Assert.Null(item.Stem);
    }

    [Fact]
    public async Task SrsDue_shows_full_stem_for_pro()
    {
        (Guid topicId, var q) = await SeedCanonicalFreeTopicAsync();
        Guid userId = Guid.NewGuid();
        await SeedWrongStudyStateAsync(userId, q.OpenQuestionId, topicId);
        await SetNextDueInPastAsync(userId, q.OpenQuestionId);

        AuthenticateAs("platform-participant", userId);
        EntitlementChecker.GrantAll(); // PRO sees full content
        IReadOnlyList<SrsDueItemDto> due =
            await ReadResultAsync<IReadOnlyList<SrsDueItemDto>>(await Client.GetAsync("/trainer/srs/due"));

        SrsDueItemDto item = Assert.Single(due);
        Assert.False(item.IsLocked);
        Assert.Equal(TrainerQuestionFixtures.OpenStem, item.Stem);
    }

    // --- recompute-free-samples backfill CLI restores flags ---

    [Fact]
    public async Task RecomputeBackfill_restores_free_sample_flags()
    {
        (_, var q) = await SeedCanonicalFreeTopicAsync();

        // Simulate a freshly-migrated bank: every question locked (is_free_sample=false).
        await ExecuteInDbAsync(db =>
            db.Database.ExecuteSqlAsync($"UPDATE trainer.trainer_questions SET is_free_sample = false"));

        Assert.False(await IsFreeSampleAsync(q.SingleQuestionId)); // precondition

        using (IServiceScope scope = Factory.Services.CreateScope())
        {
            int topics = await RecomputeFreeSamplesCli.RecomputeAllTopicsAsync(
                scope.ServiceProvider, CancellationToken.None);
            Assert.True(topics > 0);
        }

        // The canonical free allocation is restored (single + multi free; exact + open not).
        Assert.True(await IsFreeSampleAsync(q.SingleQuestionId));
        Assert.True(await IsFreeSampleAsync(q.MultiQuestionId));
        Assert.False(await IsFreeSampleAsync(q.ExactQuestionId));
        Assert.False(await IsFreeSampleAsync(q.OpenQuestionId));
    }

    // --- helpers ---

    private async Task<QuestionListDto> GetListAsFreeParticipantAsync(Guid topicId)
    {
        AuthenticateAs("platform-participant");
        EntitlementChecker.DenyAll();
        HttpResponseMessage response = await Client.GetAsync($"/trainer/topics/{topicId}/questions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<QuestionListDto>(response);
    }

    private async Task<Guid> CreateClosedAsync(Guid bankId, string stem)
    {
        AuthenticateAsAdmin();
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{bankId}/questions",
            new QuestionInputDto(stem, "SINGLE_CHOICE", null, null, "JUNIOR", null,
                [new QuestionOptionInputDto("a", true), new QuestionOptionInputDto("b", false)]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<QuestionIdResponse>(response)).Id;
    }

    private async Task<(Guid TopicId, Guid BankId)> SeedPublishedTopicAndBankAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createTopic = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "fs-topic", "Тема", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createTopic)).TopicId;

        HttpResponseMessage addBank = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("PAID", null)); // tier irrelevant to access now (#674)
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(addBank)).BankId;

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return (topicId, bankId);
    }

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedCanonicalFreeTopicAsync()
    {
        (Guid topicId, Guid bankId) = await SeedPublishedTopicAndBankAsync();
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        return (topicId, questions);
    }

    private Task SeedWrongStudyStateAsync(Guid userId, Guid questionId, Guid topicId) =>
        ExecuteInDbAsync(async db =>
        {
            QuestionStudyState state = QuestionStudyState.Create(userId, questionId, topicId);
            state.RecordTestResult(correct: false, DateTimeOffset.UtcNow); // WRONG
            await db.QuestionStudyStates.AddAsync(state);
            await db.SaveChangesAsync();
            return state.Id;
        });

    private Task SetNextDueInPastAsync(Guid userId, Guid questionId) =>
        ExecuteInDbAsync(async db =>
            await db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.question_study_states SET next_due_at = {DateTimeOffset.UtcNow.AddDays(-1)} WHERE user_id = {userId} AND question_id = {questionId}"));

    private Task<bool> IsFreeSampleAsync(Guid questionId) =>
        ExecuteInDbAsync(db => db.TrainerQuestions
            .AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => q.IsFreeSample)
            .SingleAsync());
}
