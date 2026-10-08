using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts.Admin;
using AuthService.IntegrationTests.Infrastructure;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class AdminBulkLockoutTests : IntegrationTestsBase
{
    public AdminBulkLockoutTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task BulkLockout_PastLockoutEnd_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "bulk-lockout-admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/admin/bulk/lockout",
            new AdminBulkLockoutRequest(
                [Guid.NewGuid()],
                IsLocked: true,
                LockoutEnd: DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
