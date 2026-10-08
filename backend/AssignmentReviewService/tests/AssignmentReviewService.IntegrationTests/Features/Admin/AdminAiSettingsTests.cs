using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Contracts.AiSettings;
using AssignmentReviewService.IntegrationTests.Infrastructure;

namespace AssignmentReviewService.IntegrationTests.Features.Admin;

public sealed class AdminAiSettingsTests : AssignmentReviewServiceTestsBase
{
    public AdminAiSettingsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Get_NoDbRow_ReturnsConfigSource()
    {
        AuthenticateAs("platform-admin");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/assignment-review/admin/ai-settings/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.NotNull(envelope?.Result);
        Assert.Equal("CONFIG", envelope!.Result!.Reviewer.Source);
        Assert.Equal("deepseek/deepseek-v4-pro", envelope.Result.Reviewer.Model);
    }

    [Fact]
    public async Task Put_ThenGet_ReturnsDatabaseSource()
    {
        AuthenticateAs("platform-admin");

        UpdateAiModelSettingsRequest req = new(
            Reviewer: new AiModelSlotInputDto("openai/gpt-4o", 0.3, 5000, 400));

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", req);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        HttpResponseMessage get = await AppHttpClient.GetAsync(
            "/assignment-review/admin/ai-settings/");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var envelope = await get.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.NotNull(envelope?.Result);
        Assert.Equal("DATABASE", envelope!.Result!.Reviewer.Source);
        Assert.Equal("openai/gpt-4o", envelope.Result.Reviewer.Model);
        Assert.Equal(0.3, envelope.Result.Reviewer.Temperature);
        Assert.Equal(5000, envelope.Result.Reviewer.MaxOutputTokens);
    }

    [Fact]
    public async Task Put_NonAdmin_Returns403()
    {
        AuthenticateAs("platform-participant");

        UpdateAiModelSettingsRequest req = new(
            Reviewer: new AiModelSlotInputDto("openai/gpt-4o", 0.3, 5000, 400));

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", req);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidModelName_Returns400()
    {
        AuthenticateAs("platform-admin");

        UpdateAiModelSettingsRequest req = new(
            Reviewer: new AiModelSlotInputDto("", 0.3, 5000, 400));

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", req);

        Assert.NotEqual(HttpStatusCode.OK, put.StatusCode);
    }

    [Fact]
    public async Task Get_PersistsAcrossUpdate_AndCacheInvalidates()
    {
        AuthenticateAs("platform-admin");

        UpdateAiModelSettingsRequest first = new(
            Reviewer: new AiModelSlotInputDto("openai/gpt-4o", 0.1, 1000, 100));
        await AppHttpClient.PutAsJsonAsync("/assignment-review/admin/ai-settings/", first);

        // Update again — cache invalidate должен сработать.
        UpdateAiModelSettingsRequest second = new(
            Reviewer: new AiModelSlotInputDto("openai/gpt-4.1", 0.5, 2000, 200));
        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", second);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // Read updated value — cache should not stick to first.
        HttpResponseMessage get = await AppHttpClient.GetAsync(
            "/assignment-review/admin/ai-settings/");
        var envelope = await get.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.Equal("openai/gpt-4.1", envelope!.Result!.Reviewer.Model);
        Assert.Equal(0.5, envelope.Result.Reviewer.Temperature);
    }

    [Fact]
    public async Task Get_NoDbRow_ReturnsConfigBasePrompt()
    {
        AuthenticateAs("platform-admin");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/assignment-review/admin/ai-settings/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.NotNull(envelope?.Result);
        Assert.Equal("CONFIG", envelope!.Result!.ReviewerBasePrompt.Source);
        Assert.False(string.IsNullOrWhiteSpace(envelope.Result.ReviewerBasePrompt.Value));
    }

    [Fact]
    public async Task Put_ThenGet_RoundTripsBasePrompt()
    {
        AuthenticateAs("platform-admin");

        const string customPrompt = "Всегда проверяй покрытие тестами и SOLID.";
        UpdateAiModelSettingsRequest req = new(
            Reviewer: new AiModelSlotInputDto("openai/gpt-4o", 0.3, 5000, 400),
            ReviewerBasePrompt: customPrompt);

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", req);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        HttpResponseMessage get = await AppHttpClient.GetAsync(
            "/assignment-review/admin/ai-settings/");
        var envelope = await get.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.NotNull(envelope?.Result);
        Assert.Equal("DATABASE", envelope!.Result!.ReviewerBasePrompt.Source);
        Assert.Equal(customPrompt, envelope.Result.ReviewerBasePrompt.Value);
    }

    [Fact]
    public async Task Put_ReviewEnabled_RoundTrips()
    {
        AuthenticateAs("platform-admin");

        UpdateAiModelSettingsRequest enable = new(
            Reviewer: new AiModelSlotInputDto("openai/gpt-4o", 0.2, 4000, 300),
            ReviewEnabled: true);
        HttpResponseMessage putOn = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", enable);
        Assert.Equal(HttpStatusCode.OK, putOn.StatusCode);

        HttpResponseMessage get1 = await AppHttpClient.GetAsync("/assignment-review/admin/ai-settings/");
        var env1 = await get1.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.True(env1!.Result!.ReviewEnabled);

        UpdateAiModelSettingsRequest disable = enable with { ReviewEnabled = false };
        HttpResponseMessage putOff = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", disable);
        Assert.Equal(HttpStatusCode.OK, putOff.StatusCode);

        HttpResponseMessage get2 = await AppHttpClient.GetAsync("/assignment-review/admin/ai-settings/");
        var env2 = await get2.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.False(env2!.Result!.ReviewEnabled);
    }

    [Fact]
    public async Task Put_ServicePrincipal_EmptyUserId_Succeeds()
    {
        // MCP / Hermes authenticate via client_credentials — no user-claim, so
        // UserScopedData.UserId == Guid.Empty. Regression guard: the domain must
        // NOT reject the empty audit id, otherwise service-token settings updates
        // (the canonical way Hermes flips the master switch / base prompt) fail 4xx.
        AuthenticateAs("platform-admin", Guid.Empty);

        UpdateAiModelSettingsRequest req = new(
            Reviewer: new AiModelSlotInputDto("openai/gpt-4o", 0.3, 5000, 400),
            ReviewerBasePrompt: "Service-token edit.",
            ReviewEnabled: true);

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", req);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        HttpResponseMessage get = await AppHttpClient.GetAsync(
            "/assignment-review/admin/ai-settings/");
        var envelope = await get.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.NotNull(envelope?.Result);
        Assert.Equal("DATABASE", envelope!.Result!.Reviewer.Source);
        Assert.Equal("openai/gpt-4o", envelope.Result.Reviewer.Model);
        Assert.True(envelope.Result.ReviewEnabled);
        // Empty service-principal id is normalized to null, not stored as Guid.Empty.
        Assert.Null(envelope.Result.UpdatedByUserId);
    }

    [Fact]
    public async Task Put_RealAdmin_RecordsUpdatedByUserId()
    {
        Guid adminId = Guid.NewGuid();
        AuthenticateAs("platform-admin", adminId);

        UpdateAiModelSettingsRequest req = new(
            Reviewer: new AiModelSlotInputDto("openai/gpt-4o", 0.3, 5000, 400));

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            "/assignment-review/admin/ai-settings/", req);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        HttpResponseMessage get = await AppHttpClient.GetAsync(
            "/assignment-review/admin/ai-settings/");
        var envelope = await get.Content.ReadFromJsonAsync<EnvelopeShape>();
        Assert.NotNull(envelope?.Result);
        // A genuine user still gets attributed — audit semantics preserved.
        Assert.Equal(adminId, envelope!.Result!.UpdatedByUserId);
    }

    private sealed record EnvelopeShape(AssignmentReviewAiModelSettingsDto? Result);
}
