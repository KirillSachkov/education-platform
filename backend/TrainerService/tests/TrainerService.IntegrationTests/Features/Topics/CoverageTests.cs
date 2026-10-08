using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Progress;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Topics;

/// <summary>
///     «Освоение» = ПОКРЫТИЕ темы (#664): доля distinct ВЕРНО отвеченных вопросов из всех вопросов
///     банков темы — а не EWMA-mastery (тот взлетает до 100% с одного верного ответа). Покрытие едет
///     на <c>GET /trainer/topics</c> (<c>CoveragePercent</c>) и <c>GET /trainer/progress</c>.
///     Канонический банк = 4 вопроса, поэтому 1 верный = 25% покрытия (≪ 80% «Хорошо изучена»).
/// </summary>
public sealed class CoverageTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Coverage_is_zero_before_any_answer()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();

        TopicListItemDto topic = await GetTopicAsync(topicId);
        Assert.Equal(0, topic.CoveragePercent);
        Assert.Equal(0, topic.MasteryPercent);
    }

    [Fact]
    public async Task One_correct_answer_is_partial_coverage_not_full_mastery_illusion()
    {
        (Guid topicId, SessionDto session, var q) = await StartDrillAsync();

        // Один верный ответ из четырёх вопросов банка.
        await CheckAsync(
            session.Id, ItemFor(session, q.SingleQuestionId).Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));

        TopicListItemDto topic = await GetTopicAsync(topicId);
        // Покрытие 1/4 = 25%, НЕ 100% — это и есть фикс #664 (раньше «Освоение 100% / Хорошо изучена»).
        Assert.Equal(25, topic.CoveragePercent);
        Assert.True(topic.CoveragePercent < 80, "1 of 4 covered must be far from «Хорошо изучена» (>=80).");
        // EWMA-mastery всё ещё 100 (один верный ответ) — доказывает, что покрытие ≠ mastery.
        Assert.Equal(100, topic.MasteryPercent);
    }

    [Fact]
    public async Task Incorrect_answers_do_not_count_toward_coverage()
    {
        (Guid topicId, SessionDto session, var q) = await StartDrillAsync();

        // Верный single (+1 покрытие) + неверный multi (0 покрытия).
        await CheckAsync(
            session.Id, ItemFor(session, q.SingleQuestionId).Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));
        await CheckAsync(
            session.Id, ItemFor(session, q.MultiQuestionId).Id,
            new CheckAnswerRequest([q.MultiWrongOption], null));

        TopicListItemDto topic = await GetTopicAsync(topicId);
        Assert.Equal(25, topic.CoveragePercent); // только верный single, не неверный multi
        Assert.Equal(2, topic.AnswersCount);
    }

    [Fact]
    public async Task All_correct_answers_reach_full_coverage()
    {
        (Guid topicId, SessionDto session, var q) = await StartDrillAsync();

        // Все четыре вопроса банка отвечены верно (open грейдит фейковый AI → CORRECT/100).
        await CheckAsync(
            session.Id, ItemFor(session, q.SingleQuestionId).Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));
        await CheckAsync(
            session.Id, ItemFor(session, q.MultiQuestionId).Id,
            new CheckAnswerRequest(q.MultiCorrectOptions, null));
        await CheckAsync(
            session.Id, ItemFor(session, q.ExactQuestionId).Id,
            new CheckAnswerRequest(null, "  garbage-collector! "));
        await CheckAsync(
            session.Id, ItemFor(session, q.OpenQuestionId).Id,
            new CheckAnswerRequest(null, "Развёрнутый ответ про поколенческий GC."));

        TopicListItemDto topic = await GetTopicAsync(topicId);
        Assert.Equal(100, topic.CoveragePercent);
    }

    [Fact]
    public async Task Progress_endpoint_also_carries_coverage()
    {
        (Guid topicId, SessionDto session, var q) = await StartDrillAsync();

        await CheckAsync(
            session.Id, ItemFor(session, q.SingleQuestionId).Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));

        HttpResponseMessage response = await Client.GetAsync("/trainer/progress");
        TrainerProgressDto progress = await ReadResultAsync<TrainerProgressDto>(response);
        TopicMasteryDto topic = Assert.Single(progress.Mastery, m => m.TopicId == topicId);
        Assert.Equal(25, topic.CoveragePercent);
        Assert.Equal(100, topic.MasteryPercent); // EWMA untouched
    }

    // --- helpers (mirror DrillLoopTests) ---

    private static SessionItemDto ItemFor(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId);

    private async Task<TopicListItemDto> GetTopicAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.GetAsync("/trainer/topics");
        IReadOnlyList<TopicListItemDto> topics =
            await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(response);
        return Assert.Single(topics, t => t.Id == topicId);
    }

    private async Task<CheckAnswerResponse> CheckAsync(Guid sessionId, Guid itemId, CheckAnswerRequest request)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{sessionId}/answers/{itemId}/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<CheckAnswerResponse>(response);
    }

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "coverage-topic", "Тема покрытия", "Runtime", null, null, null, null));
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

    private async Task<(Guid TopicId, SessionDto Session, TrainerQuestionFixtures.SeededQuestions Questions)> StartDrillAsync()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) = await SeedPublishedFreeTopicAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        return (topicId, session, questions);
    }
}
