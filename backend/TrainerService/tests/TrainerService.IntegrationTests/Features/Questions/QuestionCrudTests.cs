using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Questions;

/// <summary>
///     Admin question CRUD over the trainer's own bank (#623): create → list → update → delete a
///     question, plus the per-type domain validation (SINGLE needs exactly one correct option;
///     EXACT_TEXT needs a reference). Role ADMIN; the full (answers-included) projection is admin-only.
/// </summary>
public sealed class QuestionCrudTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Create_list_update_delete_round_trip()
    {
        Guid bankId = await SeedBankAsync();

        // Create a SINGLE_CHOICE question.
        Guid questionId = await CreateAsync(bankId, new QuestionInputDto(
            "Что освобождает управляемую память в .NET?",
            "SINGLE_CHOICE", null, "GC освобождает память автоматически.", "JUNIOR", "memory",
            [new QuestionOptionInputDto("Сборщик мусора", true), new QuestionOptionInputDto("Деструктор", false)]));

        // List → full admin projection with options incl. isCorrect.
        IReadOnlyList<QuestionAdminDto> afterCreate = await ListAsync(bankId);
        QuestionAdminDto created = Assert.Single(afterCreate);
        Assert.Equal(questionId, created.Id);
        Assert.Equal("SINGLE_CHOICE", created.Type);
        Assert.Equal(2, created.Options.Count);
        Assert.Single(created.Options, o => o.IsCorrect);

        // Update → new stem + difficulty.
        HttpResponseMessage update = await Client.PutAsJsonAsync(
            $"/trainer/questions/{questionId}",
            new QuestionInputDto(
                "Обновлённый вопрос про GC?", "SINGLE_CHOICE", null, null, "MIDDLE", "memory",
                [new QuestionOptionInputDto("Да", true), new QuestionOptionInputDto("Нет", false)]));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        QuestionAdminDto updated = Assert.Single(await ListAsync(bankId));
        Assert.Equal("Обновлённый вопрос про GC?", updated.Stem);
        Assert.Equal("MIDDLE", updated.Difficulty);

        // Delete → empty list.
        HttpResponseMessage delete = await Client.DeleteAsync($"/trainer/questions/{questionId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Empty(await ListAsync(bankId));
    }

    [Fact]
    public async Task Create_single_choice_with_two_correct_options_is_rejected()
    {
        Guid bankId = await SeedBankAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{bankId}/questions",
            new QuestionInputDto(
                "Сколько правильных?", "SINGLE_CHOICE", null, null, null, null,
                [new QuestionOptionInputDto("A", true), new QuestionOptionInputDto("B", true)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.question.single.one.correct", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Create_exact_text_without_reference_is_rejected()
    {
        Guid bankId = await SeedBankAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{bankId}/questions",
            new QuestionInputDto(
                "Назовите механизм (англ.)?", "EXACT_TEXT", ReferenceAnswer: null, null, null, null, Options: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.question.reference.required", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Create_with_unknown_type_is_rejected()
    {
        Guid bankId = await SeedBankAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{bankId}/questions",
            new QuestionInputDto("Вопрос", "MYSTERY_TYPE", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.question.invalid.type", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Create_into_unknown_bank_returns_404()
    {
        AuthenticateAsAdmin();
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{Guid.NewGuid()}/questions",
            new QuestionInputDto("Вопрос", "OPEN_TEXT", "эталон", null, null, null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("trainer.bank.not.found", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task NonAdmin_cannot_create_question()
    {
        Guid bankId = await SeedBankAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topic-banks/{bankId}/questions",
            new QuestionInputDto("Вопрос", "OPEN_TEXT", "эталон", null, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- helpers ---

    private async Task<Guid> CreateAsync(Guid bankId, QuestionInputDto input)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/trainer/topic-banks/{bankId}/questions", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<QuestionIdResponse>(response)).Id;
    }

    private async Task<IReadOnlyList<QuestionAdminDto>> ListAsync(Guid bankId)
    {
        HttpResponseMessage response = await Client.GetAsync($"/trainer/topic-banks/{bankId}/questions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<IReadOnlyList<QuestionAdminDto>>(response);
    }

    /// <summary>Creates an empty FREE bank under a fresh published topic. Leaves the client admin.</summary>
    private async Task<Guid> SeedBankAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createTopic = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "crud-topic", "Тема CRUD", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createTopic)).TopicId;

        HttpResponseMessage addBank = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Assert.Equal(HttpStatusCode.OK, addBank.StatusCode);
        return (await ReadResultAsync<TopicBankIdResponse>(addBank)).BankId;
    }
}
