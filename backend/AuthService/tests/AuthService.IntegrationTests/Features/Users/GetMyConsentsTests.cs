using System.Net;
using System.Net.Http.Json;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth.Authorization;

namespace AuthService.IntegrationTests.Features.Users;

/// <summary>
///     Тесты эндпоинта GET /users/me/consents (#49) — UI «мои согласия» в Settings.
/// </summary>
[Collection(nameof(IntegrationTestFixture))]
public class GetMyConsentsTests : IntegrationTestsBase
{
    public GetMyConsentsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyConsents_Unauthenticated_Returns401()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me/consents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyConsents_AuthenticatedUserWithoutConsents_ReturnsEmptyList()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Test User", "no-consents@test.com", PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Test User", "no-consents@test.com", PlatformRoles.PARTICIPANT);

        ConsentsEnvelope? body = await GetConsentsAsync();

        Assert.NotNull(body);
        Assert.NotNull(body.Result);
        Assert.NotNull(body.Result.Consents);
        Assert.Empty(body.Result.Consents);
    }

    [Fact]
    public async Task GetMyConsents_AuthenticatedUserWithConsents_ReturnsAllRecordsSortedDescByAcceptedAt()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Test User", "with-consents@test.com", PlatformRoles.PARTICIPANT);

        DateTime t1 = DateTime.UtcNow.AddHours(-2);
        DateTime t2 = DateTime.UtcNow.AddHours(-1);
        DateTime t3 = DateTime.UtcNow;

        await SeedConsentAsync(userId, ConsentType.OFFER, "v1", t1);
        await SeedConsentAsync(userId, ConsentType.PERSONAL_DATA, "v1", t2);
        await SeedConsentAsync(userId, ConsentType.MARKETING, "v1", t3);

        AuthorizeAs(userId, "Test User", "with-consents@test.com", PlatformRoles.PARTICIPANT);

        ConsentsEnvelope? body = await GetConsentsAsync();

        Assert.NotNull(body?.Result);
        Assert.Equal(3, body.Result.Consents.Count);

        // Сортировка от свежего к старому: MARKETING → PERSONAL_DATA → OFFER
        Assert.Equal("MARKETING", body.Result.Consents[0].ConsentType);
        Assert.Equal("PERSONAL_DATA", body.Result.Consents[1].ConsentType);
        Assert.Equal("OFFER", body.Result.Consents[2].ConsentType);

        // DocumentVersion заполнен
        Assert.All(body.Result.Consents, c => Assert.Equal("v1", c.DocumentVersion));
    }

    [Fact]
    public async Task GetMyConsents_ReturnsOnlyOwnUserConsents()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();

        await SeedUserAsync(userA, "A", "user-a-consents@test.com", PlatformRoles.PARTICIPANT);
        await SeedUserAsync(userB, "B", "user-b-consents@test.com", PlatformRoles.PARTICIPANT);

        await SeedConsentAsync(userA, ConsentType.OFFER, "v1", DateTime.UtcNow);
        await SeedConsentAsync(userB, ConsentType.OFFER, "v1", DateTime.UtcNow);
        await SeedConsentAsync(userB, ConsentType.PERSONAL_DATA, "v1", DateTime.UtcNow);

        AuthorizeAs(userA, "A", "user-a-consents@test.com", PlatformRoles.PARTICIPANT);

        ConsentsEnvelope? body = await GetConsentsAsync();

        Assert.NotNull(body?.Result);
        Assert.Single(body.Result.Consents);
        Assert.Equal("OFFER", body.Result.Consents[0].ConsentType);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private async Task<ConsentsEnvelope?> GetConsentsAsync()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/users/me/consents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ConsentsEnvelope>();
    }

    private async Task SeedConsentAsync(Guid userId, ConsentType type, string version, DateTime acceptedAt)
    {
        await ExecuteInDb(async db =>
        {
            db.UserConsents.Add(new UserConsent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ConsentType = type,
                DocumentVersion = version,
                AcceptedAt = acceptedAt,
                IpAddress = "127.0.0.1",
                UserAgent = "test-agent",
            });
            await db.SaveChangesAsync();
        });
    }

    private sealed record ConsentRecordView(string ConsentType, string DocumentVersion, DateTime AcceptedAt);
    private sealed record ConsentsResult(IReadOnlyList<ConsentRecordView> Consents);
    private sealed record ConsentsEnvelope(bool IsError, ConsentsResult Result);
}
