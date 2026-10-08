using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shared.AI;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Features.Questions.UseCases;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Questions;

/// <summary>
///     Admin backfill of reference answers (#691 t6): <c>POST /trainer/admin/questions/backfill-reference-answers</c>
///     fills an AI-generated draft эталон for OPEN_TEXT questions whose <c>ReferenceAnswer</c> is empty.
///     The LLM is mocked via the factory's <see cref="IAiClient"/> (the real generator runs against it).
///     Covers: fills empty; idempotent skip of already-filled; skips non-OPEN_TEXT types; admin-only
///     (403 non-admin / 401 anon); empty set → 0 без вызова LLM; bank- and topic-scoped runs.
/// </summary>
public sealed class ReferenceAnswerBackfillTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    private const string GeneratedAnswer = "GC освобождает память автоматически по поколениям 0/1/2.";

    [Fact]
    public async Task Backfill_fills_empty_reference_for_open_text_question()
    {
        StubReference(GeneratedAnswer);
        AuthenticateAsAdmin();
        Guid bankId = await SeedBankAsync("fill");
        Guid openId = await CreateOpenAsync(bankId, "Опишите, как работает поколенческий GC.");

        BackfillReferenceAnswersResponse result = await BackfillAsync();

        Assert.Equal(1, result.Generated);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.Equal(GeneratedAnswer, await ReferenceOfAsync(bankId, openId));
    }

    [Fact]
    public async Task Backfill_is_idempotent_and_skips_already_filled()
    {
        StubReference(GeneratedAnswer);
        AuthenticateAsAdmin();
        Guid bankId = await SeedBankAsync("idem");
        Guid openId = await CreateOpenAsync(bankId, "Опишите, как работает поколенческий GC.");

        // First run fills it.
        BackfillReferenceAnswersResponse first = await BackfillAsync();
        Assert.Equal(1, first.Generated);

        // Re-stub a DIFFERENT answer — if the second run regenerated, the reference would change.
        StubReference("ДРУГОЙ эталон, который не должен примениться.");
        BackfillReferenceAnswersResponse second = await BackfillAsync();

        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(0, second.Failed);
        Assert.Equal(GeneratedAnswer, await ReferenceOfAsync(bankId, openId)); // untouched
    }

    [Fact]
    public async Task Backfill_skips_non_open_text_types()
    {
        StubReference(GeneratedAnswer);
        AuthenticateAsAdmin();
        Guid bankId = await SeedBankAsync("types");
        Guid singleId = await CreateSingleAsync(bankId, "Что освобождает память в .NET?");
        Guid exactId = await CreateExactAsync(bankId, "Механизм (англ.)?", "Garbage Collector");
        Guid openId = await CreateOpenAsync(bankId, "Опишите поколенческий GC.");

        BackfillReferenceAnswersResponse result = await BackfillAsync();

        // Only the OPEN_TEXT question is generated; SINGLE/EXACT are not in the candidate set.
        Assert.Equal(1, result.Generated);
        Assert.Equal(GeneratedAnswer, await ReferenceOfAsync(bankId, openId));
        Assert.Null(await ReferenceOfAsync(bankId, singleId));            // choice → no reference
        Assert.Equal("Garbage Collector", await ReferenceOfAsync(bankId, exactId)); // EXACT untouched
    }

    [Fact]
    public async Task Backfill_with_no_open_text_returns_zero_without_calling_llm()
    {
        AuthenticateAsAdmin();
        Guid bankId = await SeedBankAsync("empty");
        await CreateSingleAsync(bankId, "Что освобождает память в .NET?"); // only a choice question

        BackfillReferenceAnswersResponse result = await BackfillAsync();

        Assert.Equal(0, result.Generated);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        await AiClient.DidNotReceive().GenerateAsync<JsonElement>(
            Arg.Any<AiGenerationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Backfill_requires_admin_role()
    {
        AuthenticateAsAdmin();
        Guid bankId = await SeedBankAsync("role");
        await CreateOpenAsync(bankId, "Опишите GC.");

        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.PostAsync(BackfillUrl(), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Backfill_anonymous_is_unauthorized()
    {
        RemoveAuthentication();
        HttpResponseMessage response = await Client.PostAsync(BackfillUrl(), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Backfill_scoped_by_bank_only_fills_that_bank()
    {
        StubReference(GeneratedAnswer);
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync("trainer-t6-bankscope", "t6 bankscope", "CSHARP");
        Guid topicId = await CreateTopicAsync(trackId, "t6-bankscope");
        Guid bankA = await AddBankAsync(topicId, "FREE");
        Guid bankB = await AddBankAsync(topicId, "PAID");
        Guid openA = await CreateOpenAsync(bankA, "Вопрос банка A про GC.");
        Guid openB = await CreateOpenAsync(bankB, "Вопрос банка B про GC.");

        BackfillReferenceAnswersResponse result = await BackfillAsync(bankId: bankA);

        Assert.Equal(1, result.Generated);
        Assert.Equal(GeneratedAnswer, await ReferenceOfAsync(bankA, openA));
        Assert.Null(await ReferenceOfAsync(bankB, openB)); // out of scope → untouched
    }

    [Fact]
    public async Task Backfill_scoped_by_topic_fills_all_banks_of_that_topic_only()
    {
        StubReference(GeneratedAnswer);
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync("trainer-t6-topicscope", "t6 topicscope", "CSHARP");

        Guid topic1 = await CreateTopicAsync(trackId, "t6-topic-1");
        Guid bankA = await AddBankAsync(topic1, "FREE");
        Guid bankB = await AddBankAsync(topic1, "PAID");
        Guid openA = await CreateOpenAsync(bankA, "Тема 1, банк A про GC.");
        Guid openB = await CreateOpenAsync(bankB, "Тема 1, банк B про GC.");

        Guid topic2 = await CreateTopicAsync(trackId, "t6-topic-2");
        Guid bankC = await AddBankAsync(topic2, "FREE");
        Guid openC = await CreateOpenAsync(bankC, "Тема 2, банк C про GC.");

        BackfillReferenceAnswersResponse result = await BackfillAsync(topicId: topic1);

        Assert.Equal(2, result.Generated); // both banks of topic1
        Assert.Equal(GeneratedAnswer, await ReferenceOfAsync(bankA, openA));
        Assert.Equal(GeneratedAnswer, await ReferenceOfAsync(bankB, openB));
        Assert.Null(await ReferenceOfAsync(bankC, openC)); // other topic → untouched
    }

    [Fact]
    public async Task Backfill_with_limit_processes_only_that_many_missing_references()
    {
        StubReference(GeneratedAnswer);
        AuthenticateAsAdmin();
        Guid bankId = await SeedBankAsync("limit");
        await CreateOpenAsync(bankId, "Открытый вопрос 1 про GC.");
        await CreateOpenAsync(bankId, "Открытый вопрос 2 про GC.");
        await CreateOpenAsync(bankId, "Открытый вопрос 3 про GC.");

        // Cap below the candidate count → only `limit` questions get an AI-generated эталон.
        BackfillReferenceAnswersResponse result = await BackfillAsync(limit: 2);

        Assert.Equal(2, result.Generated);
        int filled = (await ListAsync(bankId))
            .Count(q => string.Equals(q.ReferenceAnswer, GeneratedAnswer, StringComparison.Ordinal));
        Assert.Equal(2, filled); // exactly two filled; the third stays empty (capped)

        // A follow-up call drains the remaining one (idempotent skip of the two already filled).
        BackfillReferenceAnswersResponse second = await BackfillAsync(limit: 2);
        Assert.Equal(1, second.Generated);
        Assert.Equal(2, second.Skipped);
        Assert.Equal(3, (await ListAsync(bankId))
            .Count(q => string.Equals(q.ReferenceAnswer, GeneratedAnswer, StringComparison.Ordinal)));
    }

    // --- helpers ---

    private static string BackfillUrl(Guid? topicId = null, Guid? bankId = null, int? limit = null)
    {
        const string baseUrl = "/trainer/admin/questions/backfill-reference-answers";
        List<string> query = [];
        if (topicId is Guid t)
            query.Add($"topicId={t}");
        if (bankId is Guid b)
            query.Add($"bankId={b}");
        if (limit is int l)
            query.Add($"limit={l}");
        return query.Count == 0 ? baseUrl : $"{baseUrl}?{string.Join('&', query)}";
    }

    private async Task<BackfillReferenceAnswersResponse> BackfillAsync(
        Guid? topicId = null, Guid? bankId = null, int? limit = null)
    {
        HttpResponseMessage response = await Client.PostAsync(BackfillUrl(topicId, bankId, limit), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<BackfillReferenceAnswersResponse>(response);
    }

    /// <summary>Stubs the LLM reference-answer call (schema <c>trainer_reference_answer</c>) for the real generator.</summary>
    private void StubReference(string answer)
    {
        string json = JsonSerializer.Serialize(new { referenceAnswer = answer });
        JsonElement element = JsonDocument.Parse(json).RootElement.Clone();
        AiClient.GenerateAsync<JsonElement>(
                Arg.Is<AiGenerationRequest>(r =>
                    r.JsonSchema != null
                    && string.Equals(r.JsonSchema.Name, ReferenceAnswerGenerator.REFERENCE_ANSWER_SCHEMA_NAME, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<AiGenerationResult<JsonElement>, Error>(
                new AiGenerationResult<JsonElement>(element, "test", "gpt-4.1-mini", null, AiFinishReason.Stop)));
    }

    /// <summary>Creates a fresh track + published topic + empty FREE bank; returns the bank id. Caller must be admin.</summary>
    private async Task<Guid> SeedBankAsync(string key)
    {
        Guid trackId = await CreateTrackAsync($"trainer-t6-{key}", $"t6 {key}", "CSHARP");
        Guid topicId = await CreateTopicAsync(trackId, $"t6-{key}");
        return await AddBankAsync(topicId, "FREE");
    }

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, $"Тема {slug}", "Runtime", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task<Guid> AddBankAsync(Guid topicId, string tier)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest(tier, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicBankIdResponse>(response)).BankId;
    }

    private async Task<Guid> CreateOpenAsync(Guid bankId, string stem, string? reference = null)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{bankId}/questions",
            new QuestionInputDto(stem, "OPEN_TEXT", reference, null, "MIDDLE", "memory", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<QuestionIdResponse>(response)).Id;
    }

    private async Task<Guid> CreateSingleAsync(Guid bankId, string stem)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{bankId}/questions",
            new QuestionInputDto(stem, "SINGLE_CHOICE", null, null, "JUNIOR", "memory",
                [new QuestionOptionInputDto("Верно", true), new QuestionOptionInputDto("Неверно", false)]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<QuestionIdResponse>(response)).Id;
    }

    private async Task<Guid> CreateExactAsync(Guid bankId, string stem, string reference)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{bankId}/questions",
            new QuestionInputDto(stem, "EXACT_TEXT", reference, null, "JUNIOR", "memory", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<QuestionIdResponse>(response)).Id;
    }

    private async Task<IReadOnlyList<QuestionAdminDto>> ListAsync(Guid bankId)
    {
        HttpResponseMessage response = await Client.GetAsync($"/trainer/topic-banks/{bankId}/questions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<IReadOnlyList<QuestionAdminDto>>(response);
    }

    private async Task<string?> ReferenceOfAsync(Guid bankId, Guid questionId) =>
        (await ListAsync(bankId)).Single(q => q.Id == questionId).ReferenceAnswer;
}
