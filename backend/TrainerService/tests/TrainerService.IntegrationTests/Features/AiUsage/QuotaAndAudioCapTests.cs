using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel;
using Shared.AI;
using TrainerService.Contracts.Limits;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Configuration;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain;
using TrainerService.Domain.AiUsage;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.AiUsageQuota;

/// <summary>
///     Per-user AI-usage quota + server-side audio cap + transcription cost estimate (#614 C2):
///     <list type="bullet">
///         <item>(a) a Free user's session never contains OPEN_TEXT (#674) → the inline open-grade path is unreachable;</item>
///         <item>(b) an admin bypasses the quota entirely;</item>
///         <item>(c) a Pro user on an unlimited dimension (limit &lt;= 0) is never blocked;</item>
///         <item>(d) an oversized voice file (over the small MaxAudioBytes cap) → 400 trainer.transcribe.too_long;</item>
///         <item>(e) a voice answer writes a TRANSCRIPTION ledger row with a NON-zero cost derived from the segment duration;</item>
///         <item>(f) a voice answer consumes its real audio length (in MINUTES) into the VOICE quota → surfaced by GET /me/limits (#663);</item>
///         <item>(g) voice quota is reserved atomically before STT, preventing concurrent overspend;</item>
///         <item>(h) authoritative STT duration over the server cap is rejected.</item>
///     </list>
///     Low limits + a small audio cap are injected by <see cref="IntegrationTestsWebFactory"/> via in-memory config;
///     the quota counter is backed by <see cref="FakeQuotaRedis"/> (in-memory INCR/DECR). The quota is per-USER-per-day,
///     not per-session — and a LEARN session over the 4-question fixture has exactly ONE OPEN_TEXT item, so the
///     cap is exercised by grading the open item in a FRESH LEARN session per consumption.
/// </summary>
public sealed class QuotaAndAudioCapTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    // --- (a) Free user is blocked from the inline open-answer grade (the #623 free-gate) ---

    [Fact]
    public async Task FreeUser_learn_session_excludes_open_text_so_inline_open_grade_is_unreachable()
    {
        // #674 monetisation: a free user's session draws only free samples, and OPEN_TEXT is never a free
        // sample — so the open item never appears in a non-PRO session and the inline open-grade path
        // cannot be reached. (The CheckAnswer PRO-gate remains as defense-in-depth for stray open items.)
        EntitlementChecker.DenyAll();
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync("free-gate-topic");

        AuthenticateAs("platform-participant");
        SessionDto session = await StartLearnSessionAsync(topicId);

        Assert.NotEmpty(session.Items); // free closed samples are still offered
        Assert.DoesNotContain(
            session.Items, i => string.Equals(i.QuestionType, "OPEN_TEXT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FreeUser_auto_graded_choice_answer_is_not_pro_gated()
    {
        // Closed questions (choice/exact) are free — only the inline open-grade path is PRO-gated (#623).
        // A free user's SINGLE_CHOICE answer is graded deterministically and always succeeds.
        EntitlementChecker.DenyAll();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("free-choice-topic");

        AuthenticateAs("platform-participant");
        SessionDto session = await StartLearnSessionAsync(topicId);
        SessionItemDto single = session.Items.Single(x => x.QuestionId == q.SingleQuestionId);
        HttpResponseMessage choice = await PostCheckAsync(
            session.Id, single.Id, new CheckAnswerRequest([q.SingleCorrectOption], null));
        Assert.Equal(HttpStatusCode.OK, choice.StatusCode);
    }

    // --- (b) admin is under the Pro tier — NO quota exemption (#568); open-grade Pro=0 (unlimited) in test ---

    [Fact]
    public async Task Admin_uses_pro_tier_open_grade_unlimited()
    {
        // Лимиты действуют для всех, включая админа (#568): админ — Pro-тир, а не bypass. Грейдит сверх
        // Free-cap не из-за обхода квоты, а потому что Pro open-grade=0 (unlimited) в тест-конфиге.
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync("admin-protier-topic");

        int attempts = IntegrationTestsWebFactory.TestFreeOpenGradesPerDay + 2;
        for (int i = 0; i < attempts; i++)
        {
            AuthenticateAsAdmin();
            HttpResponseMessage ok = await GradeOneOpenAnswerAsAdminAsync(topicId, $"Админ ответ {i}.");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
    }

    // --- (c) Pro user on an unlimited dimension (limit <= 0) is never blocked ---

    [Fact]
    public async Task ProUser_unlimited_open_grade_dimension_never_blocks()
    {
        // Pro tier OpenGradesPerDay = 0 (unlimited) in the test config. GrantAll() = PRO baseline; a Pro
        // user grades more open answers than the Free cap with no 403.
        EntitlementChecker.GrantAll();
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync("pro-unlimited-topic");

        int attempts = IntegrationTestsWebFactory.TestFreeOpenGradesPerDay + 2;
        for (int i = 0; i < attempts; i++)
        {
            HttpResponseMessage ok = await GradeOneOpenAnswerAsync(topicId, $"PRO ответ {i}.");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
    }

    // --- (d) oversized audio → trainer.transcribe.too_long ---

    [Fact]
    public async Task SubmitVoiceAnswer_over_the_audio_cap_returns_too_long()
    {
        StubTranscription("gpt-4o-mini-transcribe", "распознанный текст", durationSeconds: 30);
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("too-long-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId, "too-long-mock");
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        // Over the small MaxAudioBytes cap (4096) but under the 25 MiB Whisper limit → too_long.
        byte[] tooLong = AudioBytes((int)IntegrationTestsWebFactory.TestMaxAudioBytes * 2);
        HttpResponseMessage response = await PostVoiceAsync(session.Id, open.Id, tooLong, "audio/webm");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.transcribe.too_long", await ReadErrorCodeAsync(response));
    }

    // --- (e) transcription ledger row carries a non-zero cost derived from segment duration ---

    [Fact]
    public async Task SubmitVoiceAnswer_writes_TRANSCRIPTION_row_with_cost_from_segment_duration()
    {
        // 120s of audio @ ₽1.15/min (default TranscriptionPerMinuteRub) = 2 min * 1.15 = ₽2.30 → 2_300_000 micro-rub.
        const double durationSeconds = 120;
        const long expectedMicroRub = 2_300_000L;

        StubTranscription("gpt-4o-mini-transcribe", "Поколенческий GC: поколения 0, 1, 2.", durationSeconds);
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("cost-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId, "cost-mock");
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
        Assert.Equal(expectedMicroRub, row.CostMicroRub);
        Assert.Equal(session.Id, row.SessionId);
    }

    // --- (f) a voice answer consumes its real audio length in MINUTES into the VOICE quota (#663) ---

    [Fact]
    public async Task SubmitVoiceAnswer_consumes_audio_minutes_and_reports_them_in_limits()
    {
        // 200s of audio → ceil(200 / 60) = 4 minutes consumed from the Pro 5-min monthly budget (#663).
        StubTranscription("gpt-4o-mini-transcribe", "Поколенческий GC.", durationSeconds: 200);
        AuthenticateAsAdmin(); // admin = Pro tier (limits apply, no bypass — #568)
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("voice-minutes-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId, "voice-minutes-mock");

        Assert.Equal(HttpStatusCode.OK, (await SubmitOneVoiceAsync(mockId, q)).StatusCode);

        TrainerLimitsDto limits = await ReadResultAsync<TrainerLimitsDto>(
            await Client.GetAsync("/trainer/me/limits"));
        Assert.Equal(IntegrationTestsWebFactory.TestProVoiceMinutesPerMonth, limits.Voice.Limit); // 5 min cap
        Assert.Equal(4, limits.Voice.Used);                                                       // ceil(200s / 60)
    }

    // --- (g) the monthly voice budget is reserved before STT ---

    [Fact]
    public async Task SubmitVoiceAnswer_exhausts_the_monthly_voice_minute_budget()
    {
        // Pro budget = 300s, max answer = 200s. #1 reserves 200 and succeeds; #2 cannot reserve another
        // 200 before STT, so the paid call is blocked instead of overrunning the budget.
        StubTranscription("gpt-4o-mini-transcribe", "ответ", durationSeconds: 200);
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("voice-budget-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId, "voice-budget-mock");

        Assert.Equal(HttpStatusCode.OK, (await SubmitOneVoiceAsync(mockId, q)).StatusCode);
        HttpResponseMessage blocked = await SubmitOneVoiceAsync(mockId, q);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("trainer.quota.exceeded", await ReadErrorCodeAsync(blocked));
    }

    [Fact]
    public async Task Voice_reservation_is_atomic_under_parallel_requests()
    {
        var redis = new FakeQuotaRedis();
        var options = new TrainerAiOptions();
        options.Limits.Pro.VoiceMinutesPerMonth = IntegrationTestsWebFactory.TestProVoiceMinutesPerMonth;
        var quota = new TrainerQuotaService(
            redis.Multiplexer,
            Options.Create(options),
            NullLogger<TrainerQuotaService>.Instance);

        Result<VoiceQuotaReservation, Error>[] results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => quota.TryReserveVoiceAsync(
                CurrentUserId,
                hasPro: true,
                IntegrationTestsWebFactory.TestMaxVoiceAnswerSeconds,
                CancellationToken.None)));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(19, results.Count(r => r.IsFailure));
    }

    // --- (h) post-STT duration is authoritative ---

    [Fact]
    public async Task SubmitVoiceAnswer_over_duration_cap_is_rejected_after_transcription()
    {
        StubTranscription(
            "gpt-4o-mini-transcribe",
            "слишком длинный ответ",
            durationSeconds: IntegrationTestsWebFactory.TestMaxVoiceAnswerSeconds + 1,
            includeSegments: false);
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("voice-duration-cap-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId, "voice-duration-cap-mock");

        HttpResponseMessage response = await SubmitOneVoiceAsync(mockId, q);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.transcribe.too_long", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task SubmitVoiceAnswer_rejects_non_finite_provider_duration()
    {
        StubTranscription(
            "gpt-4o-mini-transcribe",
            "ответ",
            durationSeconds: double.NaN,
            includeSegments: false);
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("voice-invalid-duration-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId, "voice-invalid-duration-mock");

        HttpResponseMessage response = await SubmitOneVoiceAsync(mockId, q);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("trainer.transcribe.failed", await ReadErrorCodeAsync(response));

        TrainerLimitsDto limits = await ReadResultAsync<TrainerLimitsDto>(
            await Client.GetAsync("/trainer/me/limits"));
        Assert.Equal(4, limits.Voice.Used);
    }

    [Fact]
    public async Task SubmitVoiceAnswer_rejects_unknown_provider_duration()
    {
        StubTranscription(
            "gpt-4o-mini-transcribe",
            "ответ",
            durationSeconds: 0,
            includeSegments: false);
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("voice-unknown-duration-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId, "voice-unknown-duration-mock");

        HttpResponseMessage response = await SubmitOneVoiceAsync(mockId, q);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("trainer.transcribe.failed", await ReadErrorCodeAsync(response));

        TrainerLimitsDto limits = await ReadResultAsync<TrainerLimitsDto>(
            await Client.GetAsync("/trainer/me/limits"));
        Assert.Equal(4, limits.Voice.Used);
    }

    // --- helpers ---

    /// <summary>Starts a FRESH mock-interview session and submits a small valid voice answer on its open item.</summary>
    private async Task<HttpResponseMessage> SubmitOneVoiceAsync(Guid mockId, TrainerQuestionFixtures.SeededQuestions q)
    {
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);
        return await PostVoiceAsync(session.Id, open.Id, AudioBytes(2048), "audio/webm");
    }

    private static SessionItemDto ItemFor(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId);

    private static byte[] AudioBytes(int length) => Encoding.UTF8.GetBytes(new string('a', length));

    /// <summary>
    ///     Starts a FRESH LEARN session (as the current participant) and inline-grades its single
    ///     OPEN_TEXT item — one OPEN_GRADE quota consumption. Returns the raw /check response so the
    ///     caller asserts 200 or the 403 quota error.
    /// </summary>
    private async Task<HttpResponseMessage> GradeOneOpenAnswerAsync(Guid topicId, string answer)
    {
        AuthenticateAs("platform-participant");
        SessionDto session = await StartLearnSessionAsync(topicId);
        SessionItemDto open = session.Items.Single(i => string.Equals(i.QuestionType, "OPEN_TEXT", StringComparison.Ordinal));
        return await PostCheckAsync(session.Id, open.Id, new CheckAnswerRequest(null, answer));
    }

    /// <summary>As <see cref="GradeOneOpenAnswerAsync"/> but the session is started + graded as admin.</summary>
    private async Task<HttpResponseMessage> GradeOneOpenAnswerAsAdminAsync(Guid topicId, string answer)
    {
        SessionDto session = await StartLearnSessionAsync(topicId);
        SessionItemDto open = session.Items.Single(i => string.Equals(i.QuestionType, "OPEN_TEXT", StringComparison.Ordinal));
        return await PostCheckAsync(session.Id, open.Id, new CheckAnswerRequest(null, answer));
    }

    private async Task<SessionDto> StartLearnSessionAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/learn-sessions",
            new StartLearnSessionRequest(topicId, QuestionCount: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    private async Task<IReadOnlyList<AiUsageRecord>> GetUsageRowsAsync(Guid userId) =>
        await ExecuteInDbAsync(db => db.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync());

    private void StubTranscription(
        string model,
        string text,
        double durationSeconds,
        bool includeSegments = true)
    {
        IReadOnlyList<AiTranscriptionSegment> segments = includeSegments
            ? [new AiTranscriptionSegment(0, durationSeconds, text)]
            : [];
        AiTranscription.TranscribeAsync(Arg.Any<AiTranscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<AiTranscriptionResult, Error>(
                new AiTranscriptionResult(
                    "test", model, "ru", text,
                    segments,
                    durationSeconds)));
    }

    private async Task<HttpResponseMessage> PostCheckAsync(Guid sessionId, Guid itemId, CheckAnswerRequest request) =>
        await Client.PostAsJsonAsync($"/trainer/sessions/{sessionId}/answers/{itemId}/check", request);

    private async Task<HttpResponseMessage> PostVoiceAsync(Guid sessionId, Guid itemId, byte[] bytes, string contentType)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "audio", "answer.webm");
        return await Client.PostAsync($"/trainer/sessions/{sessionId}/answers/{itemId}/voice", form);
    }

    /// <summary>Seeds a published topic with one FREE STUDY bank (4 local questions incl. one OPEN_TEXT). Caller may be any role.</summary>
    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync(string slug)
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, "Тема квоты", "Runtime", null, null, null, null));
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
