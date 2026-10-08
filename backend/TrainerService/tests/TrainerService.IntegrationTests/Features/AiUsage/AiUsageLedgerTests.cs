using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shared.AI;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain;
using TrainerService.Domain.AiUsage;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.AiUsageLedgerFeature;

/// <summary>
///     AI-usage ledger end-to-end (#614 C1): an inline open-answer grade writes one OPEN_ANSWER_GRADE
///     row with the stubbed usage + correct micro-ruble cost; a voice answer writes one TRANSCRIPTION
///     row (no token usage, cost 0); a ledger write failure never fails the user's request (best-effort).
/// </summary>
public sealed class AiUsageLedgerTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    // Default pricing for gpt-4.1-mini (TrainerAI:Pricing defaults): ₽77 in / ₽307 out per 1M.
    // FakeOpenAnswerGrader default usage = 200 in / 50 out:
    //   in  = 200/1e6 * 77  = 0.0154  ₽
    //   out =  50/1e6 * 307 = 0.01535 ₽
    //   total = 0.03075 ₽ → 30_750 micro-rub.
    private const long ExpectedOpenGradeMicroRub = 30_750L;

    // --- (a) inline open-answer grade → one OPEN_ANSWER_GRADE row with usage + cost ---

    [Fact]
    public async Task LearnCheck_open_answer_writes_one_OPEN_ANSWER_GRADE_ledger_row_with_usage_and_cost()
    {
        Factory.OpenAnswerGrader.Usage = new AiUsage(InputTokens: 200, OutputTokens: 50, TotalTokens: 250);
        Factory.OpenAnswerGrader.Model = "gpt-4.1-mini";

        (Guid _, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        CheckAnswerResponse result = await CheckAsync(
            session.Id, open.Id, new CheckAnswerRequest(null, "Поколения 0/1/2; выжившие продвигаются."));
        Assert.Equal("CORRECT", result.Verdict);

        AiUsageRecord row = Assert.Single(await GetUsageRowsAsync(CurrentUserId));
        Assert.Equal(AiUsageOperation.OPEN_ANSWER_GRADE, row.Operation);
        Assert.Equal("gpt-4.1-mini", row.Model);
        Assert.Equal(200, row.InputTokens);
        Assert.Equal(50, row.OutputTokens);
        Assert.Equal(250, row.TotalTokens);
        Assert.Equal(ExpectedOpenGradeMicroRub, row.CostMicroRub);
        Assert.Equal(session.Id, row.SessionId);
        Assert.Equal(CurrentUserId, row.UserId);
    }

    [Fact]
    public async Task LearnCheck_auto_graded_choice_answer_writes_no_ledger_row()
    {
        // A SINGLE_CHOICE answer is graded deterministically — no LLM call → no ledger row.
        (Guid _, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);

        await CheckAsync(
            session.Id, single.Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));

        Assert.Empty(await GetUsageRowsAsync(CurrentUserId));
    }

    [Fact]
    public async Task LearnCheck_open_answer_records_unknown_model_with_zero_cost()
    {
        // A model with no configured price → cost 0 (logged warning), but the audit row is still written.
        Factory.OpenAnswerGrader.Model = "some-unpriced-model";
        Factory.OpenAnswerGrader.Usage = new AiUsage(InputTokens: 100, OutputTokens: 20, TotalTokens: 120);

        (Guid _, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        await CheckAsync(session.Id, open.Id, new CheckAnswerRequest(null, "Ответ про GC."));

        AiUsageRecord row = Assert.Single(await GetUsageRowsAsync(CurrentUserId));
        Assert.Equal("some-unpriced-model", row.Model);
        Assert.Equal(0L, row.CostMicroRub);
        Assert.Equal(100, row.InputTokens);
    }

    [Fact]
    public async Task LearnCheck_grader_failure_writes_no_ledger_row()
    {
        // Grader returns a failure Result → answer stays PENDING, no LLM bill recorded, request still 200.
        Factory.OpenAnswerGrader.FailNext = true;

        (Guid _, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{open.Id}/check",
            new CheckAnswerRequest(null, "Развёрнутый ответ про GC."));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Empty(await GetUsageRowsAsync(CurrentUserId));
    }

    // --- (b) voice answer → one TRANSCRIPTION row with provider duration even when segments are empty ---

    [Fact]
    public async Task SubmitVoiceAnswer_writes_one_TRANSCRIPTION_ledger_row_with_null_tokens_and_duration_cost()
    {
        StubTranscription("gpt-4o-mini-transcribe", "Поколенческий GC: поколения 0, 1, 2.");
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("voice-ledger-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId, "voice-ledger");
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        HttpResponseMessage response = await PostVoiceAsync(session.Id, open.Id, AudioBytes(2048), "audio/webm");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AiUsageRecord row = Assert.Single(await GetUsageRowsAsync(CurrentUserId));
        Assert.Equal(AiUsageOperation.TRANSCRIPTION, row.Operation);
        Assert.Equal("gpt-4o-mini-transcribe", row.Model);
        Assert.Null(row.InputTokens);
        Assert.Null(row.OutputTokens);
        Assert.Null(row.TotalTokens);
        Assert.Equal(575_000L, row.CostMicroRub); // 30s × ₽1.15/min
        Assert.Equal(session.Id, row.SessionId);
    }

    // --- helpers ---

    private async Task<IReadOnlyList<AiUsageRecord>> GetUsageRowsAsync(Guid userId) =>
        await ExecuteInDbAsync(db => db.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync());

    private static SessionItemDto ItemFor(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId);

    private static byte[] AudioBytes(int length) => Encoding.UTF8.GetBytes(new string('a', length));

    private void StubTranscription(string model, string text) =>
        AiTranscription.TranscribeAsync(
                NSubstitute.Arg.Any<AiTranscriptionRequest>(), NSubstitute.Arg.Any<CancellationToken>())
            .Returns(Result.Success<AiTranscriptionResult, Error>(
                new AiTranscriptionResult("test", model, "ru", text, [], 30)));

    private async Task<HttpResponseMessage> PostVoiceAsync(
        Guid sessionId, Guid itemId, byte[] bytes, string contentType)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "audio", "answer.webm");
        return await Client.PostAsync($"/trainer/sessions/{sessionId}/answers/{itemId}/voice", form);
    }

    private async Task<CheckAnswerResponse> CheckAsync(Guid sessionId, Guid itemId, CheckAnswerRequest request)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{sessionId}/answers/{itemId}/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<CheckAnswerResponse>(response);
    }

    private async Task<(Guid TopicId, SessionDto Session, TrainerQuestionFixtures.SeededQuestions Questions)> StartLearnAsync()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) = await SeedPublishedFreeTopicAsync("ledger-learn-topic");
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/learn-sessions",
            new StartLearnSessionRequest(topicId, QuestionCount: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        return (topicId, session, questions);
    }

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync(string slug)
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, "Тема лоджера", "Runtime", null, null, null, null));
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

    private async Task<Guid> CreateMockInterviewAsync(Guid topicId, string slug)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-interviews",
            new CreateMockInterviewRequest(slug, "Найм: .NET junior", null, [topicId], null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<MockInterviewIdResponse>(response)).Id;
    }

    private async Task<SessionDto> StartMockInterviewAsync(Guid mockInterviewId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/mock-interviews/{mockInterviewId}/sessions",
            new StartMockInterviewRequest(QuestionCount: null, TimeLimitSeconds: 1800));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }
}
