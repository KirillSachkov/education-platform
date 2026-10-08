using Microsoft.AspNetCore.Hosting;

namespace NotificationService.IntegrationTests.Infrastructure;

/// <summary>
///     Factory variant that leaves <c>Notifications:Unisender:WebhookSecret</c> unset, used to
///     verify the fail-closed branch of the Unisender bounce webhook (#430): with no secret
///     configured the endpoint must reject every request with 401, even a well-formed payload.
///
///     <para>
///     Not part of the shared <see cref="IntegrationTestsFixture"/> collection — a test owns one
///     instance directly (spins up its own Postgres container). The 401 is returned before any DB
///     access, so this is a lightweight standalone host.
///     </para>
/// </summary>
public sealed class NoSecretIntegrationTestsWebFactory : IntegrationTestsWebFactory
{
    // Clear the secret instead of seeding it — empty string is treated as "not configured"
    // by the endpoint's string.IsNullOrEmpty check, triggering the fail-closed 401.
    protected override void ConfigureUnisenderWebhookSecret(IWebHostBuilder builder)
    {
        builder.UseSetting("Notifications:Unisender:WebhookSecret", string.Empty);
    }
}
