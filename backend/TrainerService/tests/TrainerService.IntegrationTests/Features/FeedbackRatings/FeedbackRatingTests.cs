using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.FeedbackRatings;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain.FeedbackRatings;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.FeedbackRatings;

/// <summary>
///     POST /trainer/sessions/{sessionId}/answers/{itemId}/feedback-rating (#691 t7): a student thumbs
///     the AI разбор of an answered OPEN_TEXT item up/down. Asserts the upsert (one row), idempotent
///     re-POST, opposite toggle, own-data scoping (foreign → 404), the rateable gate (non-answered /
///     non-OPEN_TEXT → 409), validation (bad rating → 400) and anonymous → 401.
/// </summary>
public sealed class FeedbackRatingTests(IntegrationTestsWebFactory factory)
    : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Rate_up_writes_one_row_and_echoes_current_rating()
    {
        (SessionDto session, SessionItemDto openItem) = await StartAndAnswerOpenAsync();

        AiFeedbackRatingDto rating = await RateAsync(session.Id, openItem.Id, "UP");

        Assert.Equal(openItem.Id, rating.ItemId);
        Assert.Equal("UP", rating.Rating);

        Assert.Equal(1, await CountRatingsAsync());
        Assert.Equal(FeedbackRating.UP, await SingleRatingAsync());
    }

    [Fact]
    public async Task Rate_same_again_is_idempotent_still_one_row()
    {
        (SessionDto session, SessionItemDto openItem) = await StartAndAnswerOpenAsync();

        await RateAsync(session.Id, openItem.Id, "UP");
        AiFeedbackRatingDto second = await RateAsync(session.Id, openItem.Id, "UP");

        Assert.Equal("UP", second.Rating);
        Assert.Equal(1, await CountRatingsAsync());
        Assert.Equal(FeedbackRating.UP, await SingleRatingAsync());
    }

    [Fact]
    public async Task Rate_opposite_toggles_in_place_still_one_row()
    {
        (SessionDto session, SessionItemDto openItem) = await StartAndAnswerOpenAsync();

        await RateAsync(session.Id, openItem.Id, "UP");
        AiFeedbackRatingDto toggled = await RateAsync(session.Id, openItem.Id, "DOWN");

        Assert.Equal("DOWN", toggled.Rating);
        Assert.Equal(1, await CountRatingsAsync());
        Assert.Equal(FeedbackRating.DOWN, await SingleRatingAsync());
    }

    [Fact]
    public async Task Foreign_session_item_returns_404()
    {
        // User A starts + answers as the default participant.
        (SessionDto session, SessionItemDto openItem) = await StartAndAnswerOpenAsync();

        // User B (different id) tries to rate A's item → own-data scoping makes it indistinguishable → 404.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{openItem.Id}/feedback-rating",
            new RateAiFeedbackRequest("UP"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountRatingsAsync());
    }

    [Fact]
    public async Task Unknown_item_returns_404()
    {
        (SessionDto session, _) = await StartAndAnswerOpenAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{Guid.NewGuid()}/feedback-rating",
            new RateAiFeedbackRequest("UP"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        string code = await ReadErrorCodeAsync(response);
        Assert.Equal("trainer.session.item.not.found", code);
    }

    [Fact]
    public async Task Non_answered_item_returns_409_not_rateable()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) =
            await SeedPublishedFreeTopicAsync("rate-pending-topic");
        AuthenticateAs("platform-participant");
        SessionDto session = await StartLearnAsync(topicId);

        // The OPEN_TEXT item exists but was never answered → no AI разбор → 409 not.rateable.
        SessionItemDto open = session.Items.Single(i => i.QuestionId == questions.OpenQuestionId);
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{open.Id}/feedback-rating",
            new RateAiFeedbackRequest("UP"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("trainer.feedback_rating.not.rateable", await ReadErrorCodeAsync(response));
        Assert.Equal(0, await CountRatingsAsync());
    }

    [Fact]
    public async Task Non_open_text_item_returns_409_not_rateable()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) =
            await SeedPublishedFreeTopicAsync("rate-choice-topic");
        AuthenticateAs("platform-participant");
        SessionDto session = await StartLearnAsync(topicId);

        // Answer the auto-graded SINGLE_CHOICE item (no «Разбор ИИ») then try to rate it → 409.
        SessionItemDto single = session.Items.Single(i => i.QuestionId == questions.SingleQuestionId);
        await CheckAsync(session.Id, single.Id, new CheckAnswerRequest([questions.SingleCorrectOption], null));

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{single.Id}/feedback-rating",
            new RateAiFeedbackRequest("DOWN"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("trainer.feedback_rating.not.rateable", await ReadErrorCodeAsync(response));
        Assert.Equal(0, await CountRatingsAsync());
    }

    [Fact]
    public async Task Invalid_rating_value_returns_400()
    {
        (SessionDto session, SessionItemDto openItem) = await StartAndAnswerOpenAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{openItem.Id}/feedback-rating",
            new RateAiFeedbackRequest("MEH"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.feedback_rating.invalid", await ReadErrorCodeAsync(response));
        Assert.Equal(0, await CountRatingsAsync());
    }

    [Fact]
    public async Task Anonymous_caller_returns_401()
    {
        (SessionDto session, SessionItemDto openItem) = await StartAndAnswerOpenAsync();

        RemoveAuthentication();
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{openItem.Id}/feedback-rating",
            new RateAiFeedbackRequest("UP"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    private async Task<AiFeedbackRatingDto> RateAsync(Guid sessionId, Guid itemId, string rating)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{sessionId}/answers/{itemId}/feedback-rating",
            new RateAiFeedbackRequest(rating));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<AiFeedbackRatingDto>(response);
    }

    private Task<int> CountRatingsAsync() =>
        ExecuteInDbAsync(db => db.AiFeedbackRatings.CountAsync());

    private Task<FeedbackRating> SingleRatingAsync() =>
        ExecuteInDbAsync(async db => (await db.AiFeedbackRatings.SingleAsync()).Rating);

    /// <summary>Seeds a published FREE topic, starts a LEARN session, AI-grades the OPEN_TEXT item.</summary>
    private async Task<(SessionDto Session, SessionItemDto OpenItem)> StartAndAnswerOpenAsync()
    {
        Factory.OpenAnswerGrader.Feedback = "Разбор: суть раскрыта, не хватило примера.";

        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) =
            await SeedPublishedFreeTopicAsync("rate-open-topic");
        AuthenticateAs("platform-participant");

        SessionDto session = await StartLearnAsync(topicId);
        SessionItemDto open = session.Items.Single(i => i.QuestionId == questions.OpenQuestionId);

        CheckAnswerResponse graded = await CheckAsync(
            session.Id, open.Id, new CheckAnswerRequest(null, "Поколения 0/1/2; выжившие продвигаются."));
        Assert.Equal("CORRECT", graded.Verdict); // the item now carries a final AI verdict → rateable

        return (session, open);
    }

    private async Task<SessionDto> StartLearnAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/learn-sessions",
            new StartLearnSessionRequest(topicId, QuestionCount: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    private async Task<CheckAnswerResponse> CheckAsync(Guid sessionId, Guid itemId, CheckAnswerRequest request)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{sessionId}/answers/{itemId}/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<CheckAnswerResponse>(response);
    }

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync(
        string slug)
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, "Тема оценки разбора", "Runtime", null, null, null, null));
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
