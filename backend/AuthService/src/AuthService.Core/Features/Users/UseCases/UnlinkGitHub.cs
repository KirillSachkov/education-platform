using AuthService.Core.Database;
using AuthService.Core.Features.Auth.GitHub;
using AuthService.Core.Services;
using AuthService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Validation.AspNetCore;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.Core.Features.Users.UseCases;

public sealed record UnlinkGitHubCommand : ICommand;

public sealed class UnlinkGitHubEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/users/me/github/unlink", async Task<EndpointResult<string>> (
                    [Microsoft.AspNetCore.Mvc.FromServices] UnlinkGitHubHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UnlinkGitHubCommand(), ct))
            .RequireAuthorization(policy =>
            {
                policy.AddAuthenticationSchemes(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
                    IdentityConstants.ApplicationScheme);
                policy.RequireAuthenticatedUser();
            });
    }
}

public sealed class UnlinkGitHubHandler : ICommandHandler<string, UnlinkGitHubCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly SignInManager<Account> _signInManager;
    private readonly IProfileRepository _profileRepository;
    private readonly IUserGithubOrgRepository _githubOrgRepository;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly ILogger<AuthAudit> _audit;

    public UnlinkGitHubHandler(
        UserManager<Account> userManager,
        SignInManager<Account> signInManager,
        IProfileRepository profileRepository,
        IUserGithubOrgRepository githubOrgRepository,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        UserScopedData user,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _profileRepository = profileRepository;
        _githubOrgRepository = githubOrgRepository;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _user = user;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        UnlinkGitHubCommand command,
        CancellationToken cancellationToken)
    {
        Account? user = await _userManager.FindByIdAsync(_user.UserId.ToString());
        if (user is null)
            return GeneralErrors.NotFound(_user.UserId);

        IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(user);
        UserLoginInfo? githubLogin = logins.FirstOrDefault(l => string.Equals(l.LoginProvider, GitHubRoutes.PROVIDER_NAME, StringComparison.Ordinal));

        if (githubLogin is null)
            return AuthErrors.GitHubNotLinked();

        UnitResult<Error> beginResult =
            await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (beginResult.IsFailure)
            return beginResult.Error;

        IdentityResult removeResult = await _userManager.RemoveLoginAsync(
            user, githubLogin.LoginProvider, githubLogin.ProviderKey);

        if (!removeResult.Succeeded)
        {
            _audit.LogGitHubUnlinkFailed(
                _user.UserId,
                string.Join(", ", removeResult.Errors.Select(e => e.Description)));
            return Error.Failure("github.unlink.failed", "Не удалось отвязать аккаунт GitHub");
        }

        UserProfile? profile = await _profileRepository.GetByAsync(
            p => p.Id == _user.UserId, cancellationToken);
        if (profile is not null)
            profile.ClearGitHubUrl(DateTime.UtcNow);

        await _githubOrgRepository.ReplaceAllAsync(
            user.Id, [], DateTime.UtcNow, cancellationToken);
        await _outboxService.PublishAsync(
            new UserGithubLogin(user.Id, user.UserName, []));

        UnitResult<Error> commitResult =
            await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        // Re-issue Identity cookie only after the DB/outbox transaction committed.
        await _signInManager.RefreshSignInAsync(user);

        _audit.LogGitHubUnlinked(_user.UserId);

        return "github_unlinked";
    }
}
