using Microsoft.EntityFrameworkCore;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace ProgressService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestsFixture))]
public class UserSyncIntegrationEventsTests : ProgressServiceTestsBase
{
    public UserSyncIntegrationEventsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task UserCreated_ShouldCreateProgressUser()
    {
        Guid userId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new UserCreated(userId, "student-1"));

        int usersCount = await ExecuteInDb(db =>
            db.ProgressUsers.CountAsync(x => x.UserId == userId && x.Username == "student-1"));

        Assert.Equal(1, usersCount);
    }

    [Fact]
    public async Task UserUsernameUpdated_ShouldUpdateProgressUser()
    {
        Guid userId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new UserCreated(userId, "old-name"));
        await InvokeMessageAndWaitAsync(new UserUsernameUpdated(userId, "new-name"));

        string? username = await ExecuteInDb(db =>
            db.ProgressUsers
                .Where(x => x.UserId == userId)
                .Select(x => x.Username)
                .FirstOrDefaultAsync());

        Assert.Equal("new-name", username);
    }
}
