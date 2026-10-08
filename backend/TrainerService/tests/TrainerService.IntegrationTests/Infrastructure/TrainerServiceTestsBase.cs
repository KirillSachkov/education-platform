using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentAccess.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using TrainerService.Contracts.Tracks;
using TrainerService.Infrastructure.Postgres;

namespace TrainerService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class TrainerServiceTestsBase : IAsyncLifetime
{
    public static readonly Guid DefaultUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Web defaults (camelCase) — matches the API serializer so envelope props bind.</summary>
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected readonly IntegrationTestsWebFactory Factory;
    protected readonly HttpClient Client;

    protected TrainerServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    protected Guid CurrentUserId { get; private set; } = DefaultUserId;

    /// <summary>Controllable LLM client mock (#585) — open-answer grading + aggregate feedback.</summary>
    protected Shared.AI.IAiClient AiClient => Factory.AiClient;

    /// <summary>Controllable STT client mock (#585) — Whisper transcription of spoken answers.</summary>
    protected Shared.AI.IAiTranscriptionClient AiTranscription => Factory.AiTranscription;

    /// <summary>
    ///     Controllable entitlement checker (#614) — flip the <c>cap:TRAINER_PRO</c> gate. Baseline
    ///     each test = GrantAll (PRO); a free-tier test calls <c>EntitlementChecker.DenyAll()</c>.
    /// </summary>
    protected FakeEntitlementChecker EntitlementChecker => Factory.EntitlementChecker;

    public async Task InitializeAsync()
    {
        await Factory.ResetDatabaseAsync();
        AuthenticateAs("platform-participant");
    }

    public Task DisposeAsync()
    {
        RemoveAuthentication();
        return Task.CompletedTask;
    }

    protected void AuthenticateAs(string role, Guid? userId = null)
    {
        CurrentUserId = userId ?? DefaultUserId;
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateToken(CurrentUserId, role));
    }

    protected void AuthenticateAsAdmin(Guid? userId = null)
    {
        CurrentUserId = userId ?? DefaultUserId;
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateAdminToken(CurrentUserId));
    }

    protected void RemoveAuthentication() =>
        Client.DefaultRequestHeaders.Authorization = null;

    protected async Task<T> ExecuteInDbAsync<T>(Func<TrainerServiceDbContext, Task<T>> work)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrainerServiceDbContext>();
        return await work(db);
    }

    /// <summary>Reads the <c>result</c> field out of the success envelope.</summary>
    protected static async Task<T> ReadResultAsync<T>(HttpResponseMessage response)
    {
        Envelope<T>? envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json);
        Assert.NotNull(envelope);
        Assert.False(envelope!.IsError, $"Expected success envelope but got error: {envelope.Error?.GetMessage()}");
        Assert.NotNull(envelope.Result);
        return envelope.Result!;
    }

    /// <summary>
    /// Reads the first error code (<c>error.messages[0].code</c>) out of a failure envelope.
    /// Parsed off the raw JSON so we don't depend on the private-ctor round-trip of the
    /// <c>Error</c> record.
    /// </summary>
    protected static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        string raw = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(raw);
        JsonElement error = doc.RootElement.GetProperty("error");
        JsonElement messages = error.GetProperty("messages");
        Assert.True(messages.GetArrayLength() > 0, $"Error envelope had no messages: {raw}");
        return messages[0].GetProperty("code").GetString()!;
    }

    /// <summary>Raw JSON body — used for the no-leak invariant (assert correct-answer fields absent).</summary>
    protected static Task<string> ReadRawAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync();

    /// <summary>
    /// Creates a track via the admin endpoint and returns its id. Topics now require a real
    /// track, so suites seed one before creating topics. Caller must already be admin-authed.
    /// </summary>
    protected async Task<Guid> CreateTrackAsync(string slug = "dotnet", string title = ".NET", string stack = "CSHARP")
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/tracks",
            new CreateTrackRequest(slug, title, stack, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TrackIdResponse>(response)).TrackId;
    }
}
