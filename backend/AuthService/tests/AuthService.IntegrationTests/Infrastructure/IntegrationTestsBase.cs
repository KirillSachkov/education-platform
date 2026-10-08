using System.Net.Http.Headers;
using AuthService.Core.Services;
using AuthService.Domain;
using AuthService.Domain.AuthorSpaces;
using AuthService.Domain.ValueObjects;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using AuthService.Infrastructure.Postgres;
using Wolverine.Testing;

namespace AuthService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestFixture))]
public abstract class IntegrationTestsBase : IAsyncLifetime
{
    private readonly Func<Task> _resetDatabase;

    protected HttpClient HttpClient { get; }
    protected IntegrationTestsWebFactory Factory { get; }
    protected TestOutboxCollector OutboxCollector => Factory.OutboxCollector;

    protected IntegrationTestsBase(IntegrationTestsWebFactory factory)
    {
        Factory = factory;
        HttpClient = factory.CreateClient();
        Services = factory.Services;
        _resetDatabase = factory.ResetDatabaseAsync;
    }

    protected IServiceProvider Services { get; init; }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() =>
        ResetStateAsync();

    private async Task ResetStateAsync()
    {
        await _resetDatabase();

        // Reset in-memory singletons so state doesn't leak between tests.
        if (Services.GetService<IAuthEmailSender>() is FakeEmailSender emailSender)
            emailSender.Clear();
        if (Services.GetService<IOtpStore>() is FakeOtpStore otpStore)
            otpStore.Clear();
        if (Services.GetService<ITelegramLinkTokenStore>() is FakeTelegramLinkTokenStore tgStore)
            tgStore.Clear();
    }

    protected async Task ExecuteInDb(Func<AuthDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        AuthDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await action(dbContext);
    }

    protected async Task<T> ExecuteInDb<T>(Func<AuthDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        AuthDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        return await action(dbContext);
    }

    /// <summary>Sets test auth header consumed by <see cref="TestAuthHandler" />.</summary>
    protected void AuthorizeAs(
        Guid? userId = null,
        string name = "Test User",
        string email = "test@example.com",
        params string[] groups)
    {
        Guid resolvedUserId = userId ?? Guid.NewGuid();
        HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                TestAuthHandler.SchemeName,
                $"{resolvedUserId}|{name}|{email}|{string.Join(",", groups)}");
    }

    protected void AuthorizeAsRaw(string userId, string name, string email, params string[] groups)
    {
        HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                TestAuthHandler.SchemeName,
                $"{userId}|{name}|{email}|{string.Join(",", groups)}");
    }

    protected void ClearAuthorization() =>
        HttpClient.DefaultRequestHeaders.Authorization = null;

    /// <summary>
    /// Ensures the given roles exist in the Identity roles table (idempotent).
    /// </summary>
    protected async Task SeedRolesAsync(params string[] roles)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        RoleManager<Role> roleManager =
            scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        foreach (string role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new Role { Id = Guid.NewGuid(), Name = role });
        }
    }

    /// <summary>
    /// Creates an Account in the Identity store.
    /// Required before inserting UserProfile (FK constraint).
    /// </summary>
    protected async Task<Account> SeedUserAsync(
        Guid userId,
        string name = "Test User",
        string email = "test@example.com",
        params string[] roles)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        RoleManager<Role> roleManager =
            scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        // Ensure roles exist
        foreach (string role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new Role { Id = Guid.NewGuid(), Name = role });
        }

        var user = new Account
        {
            Id = userId,
            UserName = email,
            DisplayName = name,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        IdentityResult createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create test user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");

        foreach (string role in roles)
            await userManager.AddToRoleAsync(user, role);

        return user;
    }

    /// <summary>
    /// Creates an Account with a password in the Identity store.
    /// </summary>
    protected async Task<Account> SeedUserWithPasswordAsync(
        Guid userId,
        string password,
        string name = "Test User",
        string email = "test@example.com",
        params string[] roles)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        RoleManager<Role> roleManager =
            scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        foreach (string role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new Role { Id = Guid.NewGuid(), Name = role });
        }

        var user = new Account
        {
            Id = userId,
            UserName = email,
            DisplayName = name,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        IdentityResult createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create test user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");

        foreach (string role in roles)
            await userManager.AddToRoleAsync(user, role);

        return user;
    }

    /// <summary>
    /// Resolves the singleton FakeEmailSender from the DI container.
    /// </summary>
    protected FakeEmailSender GetFakeEmailSender() =>
        (FakeEmailSender)Services.GetRequiredService<IAuthEmailSender>();

    protected FakeOtpStore GetFakeOtpStore() =>
        (FakeOtpStore)Services.GetRequiredService<IOtpStore>();

    protected FakeTelegramLinkTokenStore GetFakeTelegramLinkTokenStore() =>
        (FakeTelegramLinkTokenStore)Services.GetRequiredService<ITelegramLinkTokenStore>();

    /// <summary>
    /// Seeds an AuthorSpace row directly into the database (bypasses endpoint).
    /// </summary>
    protected async Task SeedAuthorSpaceAsync(Guid userId, string slug)
    {
        await ExecuteInDb(async db =>
        {
            AuthorSpaceSlug spaceSlug = AuthorSpaceSlug.Create(slug).Value;
            DateTime now = DateTime.UtcNow;

            db.AuthorSpaces.Add(new AuthorSpace
            {
                Id = userId,
                Slug = spaceSlug,
                FeatureFlags = new AuthorSpaceFeatureFlags(),
                CreatedAt = now,
                UpdatedAt = now,
            });

            await db.SaveChangesAsync();
        });
    }

    /// <summary>
    /// Creates Account + UserProfile together.
    /// </summary>
    protected async Task SeedUserWithProfileAsync(
        Guid userId,
        string name = "Test User",
        string email = "test@example.com",
        string[] roles = null!,
        UserProfile? profile = null)
    {
        await SeedUserAsync(userId, name, email, roles ?? []);

        await ExecuteInDb(async db =>
        {
            UserProfile p = profile ?? new UserProfile
            {
                Id = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            db.UserProfiles.Add(p);
            await db.SaveChangesAsync();
        });
    }
}
