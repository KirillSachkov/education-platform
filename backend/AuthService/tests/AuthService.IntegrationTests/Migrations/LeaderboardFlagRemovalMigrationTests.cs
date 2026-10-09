using System.Text.Json;
using AuthService.Domain;
using AuthService.Domain.AuthorSpaces;
using AuthService.Domain.ValueObjects;
using AuthService.Infrastructure.Postgres;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace AuthService.IntegrationTests.Migrations;

public class LeaderboardFlagRemovalMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migrate_FreshOrExistingDatabase_PreservesOtherAuthorFlagsAndIdentity(bool upgrade)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        options.UseOpenIddict<Guid>();
        await using var db = new AuthDbContext(options.Options);
        Guid userId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        DateTime createdAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        if (upgrade)
        {
            await db.GetService<IMigrator>().MigrateAsync("20260710173325_AddAvatarBindingRevision");
            db.Users.Add(new Account
            {
                Id = userId,
                UserName = "author@example.test",
                Email = "author@example.test",
                DisplayName = "Author",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            });
            db.Roles.Add(new Role { Id = roleId, Name = "platform-author", NormalizedName = "PLATFORM-AUTHOR" });
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
            db.AuthorSpaces.Add(new AuthorSpace
            {
                Id = userId,
                Slug = AuthorSpaceSlug.Create("retained-author").Value,
                Tagline = Tagline.Create("Retained tagline").Value,
                FeatureFlags = new AuthorSpaceFeatureFlags
                {
                    GitHubIntegration = true,
                    PrReviews = true,
                    AiAssistant = false,
                    CustomLanding = true,
                },
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE auth.author_spaces
                SET feature_flags = feature_flags || jsonb_build_object(
                    'Leaderboard', true, 'leaderboard', false, 'UnknownLegacyFlag', 'preserved')
                WHERE id = {userId};
                """);
            db.ChangeTracker.Clear();
        }

        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(upgrade ? 1 : 0, await db.Users.CountAsync());
        Assert.Equal(upgrade ? 1 : 0, await db.Roles.CountAsync());
        Assert.Equal(upgrade ? 1 : 0, await db.UserRoles.CountAsync());
        Assert.Equal(upgrade ? 1 : 0, await db.AuthorSpaces.CountAsync());
        if (!upgrade)
            return;

        AuthorSpace space = await db.AuthorSpaces.SingleAsync();
        Assert.Equal(userId, space.Id);
        Assert.Equal("retained-author", space.Slug.Value);
        Assert.Equal("Retained tagline", space.Tagline!.Value);
        Assert.Equal(createdAt, space.CreatedAt);
        Assert.True(space.FeatureFlags.GitHubIntegration);
        Assert.True(space.FeatureFlags.PrReviews);
        Assert.False(space.FeatureFlags.AiAssistant);
        Assert.True(space.FeatureFlags.CustomLanding);
        Assert.Equal("Author", (await db.Users.SingleAsync()).DisplayName);
        Assert.Equal("platform-author", (await db.Roles.SingleAsync()).Name);
        Assert.Equal(roleId, (await db.UserRoles.SingleAsync()).RoleId);

        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT feature_flags::text FROM auth.author_spaces";
        using JsonDocument flags = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        Assert.False(flags.RootElement.TryGetProperty("Leaderboard", out _));
        Assert.False(flags.RootElement.TryGetProperty("leaderboard", out _));
        Assert.Equal("preserved", flags.RootElement.GetProperty("UnknownLegacyFlag").GetString());
    }
}