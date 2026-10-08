using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Topics;

/// <summary>
///     Freemium gate (#614): FREE banks open to any authenticated user; PAID banks need
///     cap:TRAINER_PRO (admin bypasses). A topic whose only bank is PAID is locked for a free
///     participant (no PRO). Tests baseline = GrantAll (PRO); free-tier cases call DenyAll().
/// </summary>
public sealed class FreemiumTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Topic_with_free_samples_is_not_locked_for_participant_in_topic_list()
    {
        // #674: bank-tier no longer gates the topic list. A PAID-tier bank whose questions yield free
        // samples (JUNIOR→single, MIDDLE→multi) means the topic offers a free taste → NOT fully locked,
        // and HasFreeBank (repurposed = "has free sample") is true.
        await SeedPublishedTopicWithBankAsync("PAID");

        AuthenticateAs("platform-participant");
        EntitlementChecker.DenyAll(); // free participant — no cap:TRAINER_PRO
        HttpResponseMessage response = await Client.GetAsync("/trainer/topics");
        IReadOnlyList<TopicListItemDto> topics =
            await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(response);

        TopicListItemDto topic = Assert.Single(topics);
        Assert.True(topic.HasFreeBank);
        Assert.False(topic.IsLocked);
    }

    [Fact]
    public async Task OpenTextOnlyTopic_is_locked_for_participant_in_topic_list()
    {
        // #674: a topic whose only questions are OPEN_TEXT has ZERO free samples (OPEN_TEXT is never
        // free) → fully locked for a non-PRO participant; HasFreeBank false.
        await SeedPublishedOpenTextOnlyTopicAsync();

        AuthenticateAs("platform-participant");
        EntitlementChecker.DenyAll(); // free participant — no cap:TRAINER_PRO
        HttpResponseMessage response = await Client.GetAsync("/trainer/topics");
        IReadOnlyList<TopicListItemDto> topics =
            await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(response);

        TopicListItemDto topic = Assert.Single(topics);
        Assert.False(topic.HasFreeBank);
        Assert.True(topic.IsLocked);
    }

    [Fact]
    public async Task OpenTextOnlyTopic_is_not_locked_for_admin_in_topic_list()
    {
        // Admin resolves as PRO → never locked, even on a topic with no free samples.
        await SeedPublishedOpenTextOnlyTopicAsync();

        AuthenticateAsAdmin();
        HttpResponseMessage response = await Client.GetAsync("/trainer/topics");
        IReadOnlyList<TopicListItemDto> topics =
            await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(response);

        TopicListItemDto topic = Assert.Single(topics);
        Assert.False(topic.IsLocked);
    }

    [Fact]
    public async Task Participant_starts_session_drawing_only_free_samples_on_paid_topic()
    {
        // Bank-tier no longer gates the access path (#674): a free participant CAN start a session, but
        // it draws ONLY the free-sample questions (single JUNIOR + multi MIDDLE) — no locked / OPEN_TEXT.
        Guid topicId = await SeedPublishedTopicWithBankAsync("PAID");

        AuthenticateAs("platform-participant");
        EntitlementChecker.DenyAll(); // free participant — no cap:TRAINER_PRO
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        Assert.Equal(2, session.Items.Count); // free pool = single + multi (exact + OPEN_TEXT excluded)
        Assert.DoesNotContain(
            session.Items, i => string.Equals(i.QuestionType, "OPEN_TEXT", StringComparison.Ordinal));
        Assert.All(session.Items, i => Assert.False(i.IsLocked));
    }

    [Fact]
    public async Task Admin_can_start_session_on_paid_only_topic()
    {
        Guid topicId = await SeedPublishedTopicWithBankAsync("PAID");

        AuthenticateAsAdmin();
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        Assert.NotEmpty(session.Items);
        Assert.Equal("IN_PROGRESS", session.Status);
    }

    private async Task<Guid> SeedPublishedTopicWithBankAsync(string tier)
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "paid-topic", "Платная тема", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest(tier, null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return topicId;
    }

    private async Task<Guid> SeedPublishedOpenTextOnlyTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "open-only-topic", "Только открытые", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("PAID", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        await TrainerQuestionFixtures.SeedOpenTextOnlyAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return topicId;
    }
}
