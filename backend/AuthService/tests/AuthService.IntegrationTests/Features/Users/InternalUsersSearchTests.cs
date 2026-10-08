using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts;
using AuthService.IntegrationTests.Infrastructure;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class InternalUsersSearchTests : IntegrationTestsBase
{
    public InternalUsersSearchTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Search_WithLimitAtSharedCeiling_ReturnsOk()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        // ProgressService резолвит ростер курса именно с MAX_LIMIT (=100). Пока потолок
        // валидатора был 50, любой такой поиск падал в ToError JsonException'ом → 500.
        var request = new InternalUsersSearchRequest("admin", InternalUsersSearchRequest.MAX_LIMIT);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/internal/users/search", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithInvalidInput_ReturnsBadRequest_Not500()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        // Пустой query нарушает NotEmpty/MinimumLength — правила без .WithError (обычный текст
        // FluentValidation). Регрессия-гард: ToError должен отдать 400 validation, а не 500.
        var request = new InternalUsersSearchRequest("", InternalUsersSearchRequest.DEFAULT_LIMIT);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/internal/users/search", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
