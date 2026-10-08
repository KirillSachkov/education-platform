using AuthService.Core.Database;
using AuthService.Core.Options;
using AuthService.Domain;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using PlatformAuth.Authorization;

namespace AuthService.Core.Services;

/// <summary>
/// Synchronizes platform configuration: roles, OpenIddict applications, and the default admin account.
/// Designed to be called from a hosted service on a periodic schedule.
/// </summary>
public sealed class PlatformConfigSyncService
{
    private readonly RoleManager<Role> _roleManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly UserManager<Account> _userManager;
    private readonly IProfileRepository _profileRepository;

    public PlatformConfigSyncService(
        RoleManager<Role> roleManager,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        UserManager<Account> userManager,
        IProfileRepository profileRepository)
    {
        _roleManager = roleManager;
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _userManager = userManager;
        _profileRepository = profileRepository;
    }

    public async Task SyncAsync(
        OpenIddictOptions openIddictOptions,
        AuthServiceOptions authOptions,
        CancellationToken ct)
    {
        await SeedRolesAsync(ct);
        await SyncApplicationsAsync(openIddictOptions, ct);
        await SeedDefaultAdminAsync(authOptions, ct);
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        foreach (string roleName in PlatformRoles.All)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                await _roleManager.CreateAsync(new Role { Id = Guid.CreateVersion7(), Name = roleName });
            }
        }
    }

    private async Task SyncApplicationsAsync(OpenIddictOptions options, CancellationToken ct)
    {
        await SeedApiScopesAsync(options, ct);
        await UpsertFrontendClientAsync(options.EducationPlatform, ct);
        await UpsertServiceClientAsync(options.ServiceToService, ct);
        await UpsertAdminClientAsync(options.AdminApi, ct);
    }

    private async Task SeedApiScopesAsync(OpenIddictOptions options, CancellationToken ct)
    {
        await UpsertScopeAsync("platform", "Platform API Access",
            [options.EducationPlatform.ClientId, options.AdminApi.ClientId], ct);

        await UpsertScopeAsync("service", "Service-to-Service API Access",
            [options.ServiceToService.ClientId], ct);

        // Remove legacy "api" scope if it exists from a previous deployment
        object? legacyScope = await _scopeManager.FindByNameAsync("api", ct);
        if (legacyScope is not null)
            await _scopeManager.DeleteAsync(legacyScope, ct);
    }

    private async Task UpsertScopeAsync(
        string name, string displayName, string[] resources, CancellationToken ct)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = name,
            DisplayName = displayName,
        };

        foreach (string resource in resources)
            descriptor.Resources.Add(resource);

        object? existing = await _scopeManager.FindByNameAsync(name, ct);

        if (existing is null)
            await _scopeManager.CreateAsync(descriptor, ct);
        else
            await _scopeManager.UpdateAsync(existing, descriptor, ct);
    }

    private async Task UpsertFrontendClientAsync(OpenIddictClientOptions clientOptions, CancellationToken ct)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientOptions.ClientId,
            ClientSecret = clientOptions.Secret,
            DisplayName = clientOptions.DisplayName,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Scopes.Roles,
                OpenIddictConstants.Permissions.Prefixes.Scope + "platform",
            },
            RedirectUris = { new Uri(clientOptions.RedirectUri) },
        };

        foreach (string uri in clientOptions.PostLogoutRedirectUris)
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));

        // OpenIddict's store-agnostic API returns object? intentionally — it abstracts over
        // different store implementations (EF Core, MongoDB, etc.) without exposing the entity type.
        object? existing = await _applicationManager.FindByClientIdAsync(clientOptions.ClientId, ct);

        if (existing is null)
            await _applicationManager.CreateAsync(descriptor, ct);
        else
            await _applicationManager.UpdateAsync(existing, descriptor, ct);
    }

    private async Task UpsertServiceClientAsync(OpenIddictClientOptions clientOptions, CancellationToken ct)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientOptions.ClientId,
            ClientSecret = clientOptions.Secret,
            DisplayName = clientOptions.DisplayName,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + "service",
            },
        };

        object? existing = await _applicationManager.FindByClientIdAsync(clientOptions.ClientId, ct);

        if (existing is null)
            await _applicationManager.CreateAsync(descriptor, ct);
        else
            await _applicationManager.UpdateAsync(existing, descriptor, ct);
    }

    private async Task UpsertAdminClientAsync(OpenIddictClientOptions clientOptions, CancellationToken ct)
    {
        // Admin-tool client used by the platform MCP server. Issues tokens with
        // platform-admin role via client_credentials grant, granting access to all
        // admin HTTP endpoints. Secret must be kept server-side only.
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientOptions.ClientId,
            ClientSecret = clientOptions.Secret,
            DisplayName = clientOptions.DisplayName,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + "platform",
            },
        };

        object? existing = await _applicationManager.FindByClientIdAsync(clientOptions.ClientId, ct);

        if (existing is null)
            await _applicationManager.CreateAsync(descriptor, ct);
        else
            await _applicationManager.UpdateAsync(existing, descriptor, ct);
    }

    private async Task SeedDefaultAdminAsync(AuthServiceOptions authOptions, CancellationToken ct)
    {
        DefaultAdminOptions? adminOptions = authOptions.DefaultAdmin;
        if (adminOptions is null || string.IsNullOrWhiteSpace(adminOptions.Email))
            return;

        string email = adminOptions.Email.Trim().ToLowerInvariant();

        Account? user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new Account
            {
                Id = Guid.CreateVersion7(),
                UserName = adminOptions.Name,
                Email = email,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            IdentityResult result = await _userManager.CreateAsync(user, adminOptions.Password);
            if (!result.Succeeded)
                return;

            await _profileRepository.EnsureExistsAsync(user.Id, ct);
        }

        // Ensure all platform roles are assigned
        IList<string> currentRoles = await _userManager.GetRolesAsync(user);

        foreach (string role in PlatformRoles.All)
        {
            if (!currentRoles.Contains(role))
                await _userManager.AddToRoleAsync(user, role);
        }

    }
}
