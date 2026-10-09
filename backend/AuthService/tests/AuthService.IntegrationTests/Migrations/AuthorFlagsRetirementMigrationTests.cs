using System.Text.Json;
using AuthService.Domain;
using AuthService.Domain.AuthorSpaces;
using AuthService.Domain.ValueObjects;
using AuthService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PlatformDatabase;
using Testcontainers.PostgreSql;

namespace AuthService.IntegrationTests.Migrations;

public sealed class AuthorFlagsRetirementMigrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retirement_SupportsFreshDatabaseAndPreservesExistingAuthorData(bool? previousFlagValue)
    {
        await using PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        string connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            SearchPath = "auth,public",
        }.ConnectionString;
        DbContextOptionsBuilder<AuthDbContext> options = new();
        options.UsePlatformNpgsql(connectionString).UseOpenIddict<Guid>();
        await using AuthDbContext db = new(options.Options);
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS auth;");
        Assert.Contains(db.Database.GetMigrations(), name => name.EndsWith("_RetireAuthorRoadmapsFlag", StringComparison.Ordinal));
        bool flagValue = previousFlagValue ?? false;
        if (previousFlagValue.HasValue)
        {
            // The last original Auth migration precedes both independently owned flag retirements.
            await db.GetService<IMigrator>().MigrateAsync("20260710173325_AddAvatarBindingRevision");
        }
        else
        {
            await db.Database.MigrateAsync();
        }
        Guid authorId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;
        db.Users.Add(new Account
        {
            Id = authorId,
            UserName = "retained-author",
            Email = "retained-author@example.test",
            EmailConfirmed = true,
            DisplayName = "Retained author",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AuthorSpaces.Add(new AuthorSpace
        {
            Id = authorId,
            Slug = AuthorSpaceSlug.Create("retained-author").Value,
            Tagline = Tagline.Create("Retained author description").Value,
            LogoAssetId = Guid.CreateVersion7(),
            FeatureFlags = new AuthorSpaceFeatureFlags
            {
                GitHubIntegration = !flagValue,
                PrReviews = flagValue,
                AiAssistant = !flagValue,
                CustomLanding = flagValue,
            },
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        string flags = JsonSerializer.Serialize(new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Roadmaps"] = flagValue,
            ["Leaderboard"] = flagValue,
            ["roadmaps"] = !flagValue,
            ["leaderboard"] = !flagValue,
            ["GitHubIntegration"] = !flagValue,
            ["PrReviews"] = flagValue,
            ["AiAssistant"] = !flagValue,
            ["CustomLanding"] = flagValue,
            ["FutureRetainedFlag"] = flagValue,
        });
        if (previousFlagValue.HasValue)
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE auth.author_spaces SET feature_flags = {flags}::jsonb WHERE id = {authorId}");
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        string before = await ReadRetainedDataAsync(connection, authorId);

        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());

        Assert.Equal(before, await ReadRetainedDataAsync(connection, authorId));
        await using NpgsqlCommand command = new("SELECT feature_flags::text FROM auth.author_spaces WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", authorId);
        using JsonDocument result = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        Assert.False(result.RootElement.TryGetProperty("Roadmaps", out _));
        Assert.False(result.RootElement.TryGetProperty("Leaderboard", out _));
        Assert.False(result.RootElement.TryGetProperty("roadmaps", out _));
        Assert.False(result.RootElement.TryGetProperty("leaderboard", out _));
        if (previousFlagValue.HasValue)
            Assert.Equal(flagValue, result.RootElement.GetProperty("FutureRetainedFlag").GetBoolean());
        Assert.Equal(!flagValue, result.RootElement.GetProperty("GitHubIntegration").GetBoolean());
        Assert.Equal(flagValue, result.RootElement.GetProperty("PrReviews").GetBoolean());
        Assert.Equal(!flagValue, result.RootElement.GetProperty("AiAssistant").GetBoolean());
        Assert.Equal(flagValue, result.RootElement.GetProperty("CustomLanding").GetBoolean());
    }

    private static async Task<string> ReadRetainedDataAsync(NpgsqlConnection connection, Guid authorId)
    {
        const string sql = """
            SELECT jsonb_build_object(
                'user', to_jsonb(account),
                'space', to_jsonb(space) - 'feature_flags',
                'flags', space.feature_flags - 'Roadmaps' - 'Leaderboard' - 'roadmaps' - 'leaderboard')::text
            FROM auth.users account
            JOIN auth.author_spaces space ON space.id = account.id
            WHERE account.id = @id;
            """;
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("id", authorId);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}