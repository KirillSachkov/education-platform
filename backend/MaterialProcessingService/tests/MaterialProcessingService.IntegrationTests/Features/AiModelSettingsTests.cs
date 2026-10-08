using System.Net;
using System.Net.Http.Json;
using MaterialProcessingService.Contracts.AiSettings.Dtos;
using MaterialProcessingService.Contracts.AiSettings.Requests;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class AiModelSettingsTests : MaterialProcessingServiceTestsBase
{
    public AiModelSettingsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Get_WithoutAdmin_ReturnsForbidden()
    {
        AuthenticateAs(Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenDbEmpty_ReturnsConfigDefaults()
    {
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AiModelSettingsDto dto = await ReadResultAsync<AiModelSettingsDto>(response);

        Assert.Equal("CONFIG", dto.Source);
        Assert.Equal("test-audio-model", dto.SpeechToText.Model);
        Assert.Equal("test-chat-model", dto.TimecodeGeneration.Model);
        Assert.Null(dto.UpdatedAtUtc);
        // Авто-обработка видео включена по умолчанию, когда DB-override нет (#648).
        Assert.True(dto.AutoProcessVideosEnabled);
    }

    [Fact]
    public async Task Put_AutoProcessVideosToggle_PersistsAndReturns()
    {
        AuthenticateAsAdmin();

        UpdateAiModelSettingsRequest request = new(
            SpeechToText: new AiModelSlotDto("openai/gpt-4o-transcribe", 0, 4000, 900),
            TimecodeGeneration: new AiModelSlotDto("openai/gpt-4.1-nano", 0.1, 6000, 300),
            ContentGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.2, 8000, 300),
            AutoProcessVideosEnabled: false);

        HttpResponseMessage putResponse = await AppHttpClient.PutAsJsonAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative), request);
        putResponse.EnsureSuccessStatusCode();
        AiModelSettingsDto saved = await ReadResultAsync<AiModelSettingsDto>(putResponse);
        Assert.False(saved.AutoProcessVideosEnabled);

        HttpResponseMessage getResponse = await AppHttpClient.GetAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative));
        AiModelSettingsDto fetched = await ReadResultAsync<AiModelSettingsDto>(getResponse);
        Assert.False(fetched.AutoProcessVideosEnabled);
    }

    [Fact]
    public async Task Put_WithValidPayload_SavesAndReturnsDatabaseSource()
    {
        AuthenticateAsAdmin();

        UpdateAiModelSettingsRequest request = new(
            SpeechToText: new AiModelSlotDto("openai/gpt-4o-transcribe", 0, 4000, 900),
            TimecodeGeneration: new AiModelSlotDto("openai/gpt-4.1-nano", 0.1, 6000, 300),
            ContentGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.2, 8000, 300));

        HttpResponseMessage putResponse = await AppHttpClient.PutAsJsonAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative),
            request);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        AiModelSettingsDto saved = await ReadResultAsync<AiModelSettingsDto>(putResponse);

        Assert.Equal("DATABASE", saved.Source);
        Assert.Equal("openai/gpt-4.1-nano", saved.TimecodeGeneration.Model);
        Assert.NotNull(saved.UpdatedAtUtc);

        HttpResponseMessage getResponse = await AppHttpClient.GetAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative));
        AiModelSettingsDto fetched = await ReadResultAsync<AiModelSettingsDto>(getResponse);
        Assert.Equal("DATABASE", fetched.Source);
        Assert.Equal("openai/gpt-4.1-nano", fetched.TimecodeGeneration.Model);
    }

    [Fact]
    public async Task Put_WithEmptyModel_ReturnsValidationError()
    {
        AuthenticateAsAdmin();

        UpdateAiModelSettingsRequest request = new(
            SpeechToText: new AiModelSlotDto("", 0, 4000, 900),
            TimecodeGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.1, 6000, 300),
            ContentGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.2, 8000, 300));

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative),
            request);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected 400/422, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Put_WithOutOfRangeTemperature_ReturnsValidationError()
    {
        AuthenticateAsAdmin();

        UpdateAiModelSettingsRequest request = new(
            SpeechToText: new AiModelSlotDto("openai/gpt-4o-transcribe", 5, 4000, 900),
            TimecodeGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.1, 6000, 300),
            ContentGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.2, 8000, 300));

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative),
            request);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected 400/422, got {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData(100_001, 300)]
    [InlineData(4_000, 1_801)]
    public async Task Put_WithExcessiveResourceLimits_ReturnsValidationError(
        int maxOutputTokens,
        int timeoutSeconds)
    {
        AuthenticateAsAdmin();

        UpdateAiModelSettingsRequest request = new(
            SpeechToText: new AiModelSlotDto(
                "openai/gpt-4o-transcribe",
                0,
                maxOutputTokens,
                timeoutSeconds),
            TimecodeGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.1, 6000, 300),
            ContentGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.2, 8000, 300));

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative),
            request);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected 400/422, got {(int)response.StatusCode}");
    }
}
