using System.Net;
using System.Text;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.GitHubApp;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Integrations.GitHubApp;

/// <summary>
///     Tests на GitHub API status code mapping в GitHubAppApiClient.
///     Используем FakeHttpMessageHandler вместо WireMock — стандартный .NET-паттерн
///     для мока HttpClient: легче, без сторонних зависимостей.
/// </summary>
public sealed class GitHubAppApiClientTests
{
    private const long INSTALLATION_ID = 12345L;
    private const string TOKEN = "ghs_fake_token";

    private static IGitHubAppTokenService TokenServiceReturning(string token)
    {
        IGitHubAppTokenService svc = Substitute.For<IGitHubAppTokenService>();
        svc.GetInstallationTokenAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Result<string, Error>>(token));
        return svc;
    }

    private static GitHubAppApiClient ClientWith(FakeHttpMessageHandler handler, IGitHubAppTokenService? tokens = null)
    {
        HttpClient http = new(handler);
        return new GitHubAppApiClient(http, tokens ?? TokenServiceReturning(TOKEN), NullLogger<GitHubAppApiClient>.Instance);
    }

    [Fact]
    public async Task GetInstallation_happy_path_returns_account_login_lowercase()
    {
        FakeHttpMessageHandler handler = new(req =>
        {
            Assert.Equal($"https://api.github.com/app/installations/{INSTALLATION_ID}", req.RequestUri!.ToString());
            return Response(HttpStatusCode.OK, """{"account": {"login": "DotNet-Team", "type": "Organization"}}""");
        });

        Result<InstallationDetail, Error> result = await ClientWith(handler)
            .GetInstallationAsync(INSTALLATION_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("dotnet-team", result.Value.AccountLogin);
        Assert.Equal("Organization", result.Value.AccountType);
    }

    [Fact]
    public async Task GetInstallation_404_returns_failure()
    {
        FakeHttpMessageHandler handler = new(_ => Response(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        Result<InstallationDetail, Error> result = await ClientWith(handler)
            .GetInstallationAsync(INSTALLATION_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("github_app.api.failed", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task GetUserIdByLogin_returns_id()
    {
        FakeHttpMessageHandler handler = new(req =>
        {
            Assert.Equal("https://api.github.com/users/octocat", req.RequestUri!.ToString());
            return Response(HttpStatusCode.OK, """{"id": 583231, "login": "octocat"}""");
        });

        Result<long, Error> result = await ClientWith(handler)
            .GetUserIdByLoginAsync(INSTALLATION_ID, "octocat", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(583231L, result.Value);
    }

    [Fact]
    public async Task CreateOrgInvitation_201_returns_Created_with_invitation_id()
    {
        FakeHttpMessageHandler handler = new(_ =>
            Response(HttpStatusCode.Created, """{"id": 999, "invitee": {"login": "octocat"}}"""));

        CreateInvitationResult result = await ClientWith(handler)
            .CreateOrgInvitationAsync(INSTALLATION_ID, "dotnet-team", 583231L, CancellationToken.None);

        CreateInvitationResult.Created created = Assert.IsType<CreateInvitationResult.Created>(result);
        Assert.Equal(999L, created.InvitationId);
    }

    [Fact]
    public async Task CreateOrgInvitation_422_already_a_member_returns_AlreadyMember()
    {
        FakeHttpMessageHandler handler = new(_ => Response(
            HttpStatusCode.UnprocessableEntity,
            """{"message":"Validation Failed","errors":[{"code":"already_a_member"}]}"""));

        CreateInvitationResult result = await ClientWith(handler)
            .CreateOrgInvitationAsync(INSTALLATION_ID, "dotnet-team", 583231L, CancellationToken.None);

        Assert.IsType<CreateInvitationResult.AlreadyMember>(result);
    }

    [Fact]
    public async Task CreateOrgInvitation_404_returns_UserNotFound()
    {
        FakeHttpMessageHandler handler = new(_ => Response(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        CreateInvitationResult result = await ClientWith(handler)
            .CreateOrgInvitationAsync(INSTALLATION_ID, "dotnet-team", 583231L, CancellationToken.None);

        Assert.IsType<CreateInvitationResult.UserNotFound>(result);
    }

    [Fact]
    public async Task CreateOrgInvitation_401_invalidates_token_and_returns_TokenInvalid()
    {
        FakeHttpMessageHandler handler = new(_ => Response(HttpStatusCode.Unauthorized, "{}"));
        IGitHubAppTokenService tokens = TokenServiceReturning(TOKEN);

        CreateInvitationResult result = await ClientWith(handler, tokens)
            .CreateOrgInvitationAsync(INSTALLATION_ID, "dotnet-team", 583231L, CancellationToken.None);

        Assert.IsType<CreateInvitationResult.TokenInvalid>(result);
        tokens.Received(1).Invalidate(INSTALLATION_ID);
    }

    [Fact]
    public async Task CreateOrgInvitation_403_invalidates_token_and_returns_TokenInvalid()
    {
        FakeHttpMessageHandler handler = new(_ => Response(HttpStatusCode.Forbidden, "{}"));
        IGitHubAppTokenService tokens = TokenServiceReturning(TOKEN);

        CreateInvitationResult result = await ClientWith(handler, tokens)
            .CreateOrgInvitationAsync(INSTALLATION_ID, "dotnet-team", 583231L, CancellationToken.None);

        Assert.IsType<CreateInvitationResult.TokenInvalid>(result);
        tokens.Received(1).Invalidate(INSTALLATION_ID);
    }

    [Fact]
    public async Task CreateOrgInvitation_500_returns_UnknownFailure()
    {
        FakeHttpMessageHandler handler = new(_ => Response(HttpStatusCode.InternalServerError, """{"message":"oops"}"""));

        CreateInvitationResult result = await ClientWith(handler)
            .CreateOrgInvitationAsync(INSTALLATION_ID, "dotnet-team", 583231L, CancellationToken.None);

        CreateInvitationResult.UnknownFailure failure = Assert.IsType<CreateInvitationResult.UnknownFailure>(result);
        Assert.Contains("500", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateOrgInvitation_token_failure_returns_UnknownFailure_without_http_call()
    {
        bool httpCalled = false;
        FakeHttpMessageHandler handler = new(_ =>
        {
            httpCalled = true;
            return Response(HttpStatusCode.OK, "{}");
        });
        IGitHubAppTokenService tokens = Substitute.For<IGitHubAppTokenService>();
        tokens.GetInstallationTokenAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Result<string, Error>>(Error.Failure("token.fail", "no token")));

        CreateInvitationResult result = await ClientWith(handler, tokens)
            .CreateOrgInvitationAsync(INSTALLATION_ID, "dotnet-team", 583231L, CancellationToken.None);

        Assert.IsType<CreateInvitationResult.UnknownFailure>(result);
        Assert.False(httpCalled);
    }

    [Fact]
    public async Task IsOrgMember_active_returns_true()
    {
        FakeHttpMessageHandler handler = new(req =>
        {
            Assert.Equal("https://api.github.com/orgs/dotnet-team/memberships/octocat", req.RequestUri!.ToString());
            return Response(HttpStatusCode.OK, """{"state":"active","role":"member"}""");
        });

        Result<bool, Error> result = await ClientWith(handler)
            .IsOrgMemberAsync(INSTALLATION_ID, "dotnet-team", "octocat", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task IsOrgMember_pending_returns_false()
    {
        FakeHttpMessageHandler handler = new(_ =>
            Response(HttpStatusCode.OK, """{"state":"pending","role":"member"}"""));

        Result<bool, Error> result = await ClientWith(handler)
            .IsOrgMemberAsync(INSTALLATION_ID, "dotnet-team", "octocat", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task IsOrgMember_404_returns_false_not_failure()
    {
        FakeHttpMessageHandler handler = new(_ => Response(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        Result<bool, Error> result = await ClientWith(handler)
            .IsOrgMemberAsync(INSTALLATION_ID, "dotnet-team", "octocat", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task IsOrgMember_500_returns_failure()
    {
        FakeHttpMessageHandler handler = new(_ => Response(HttpStatusCode.InternalServerError, "{}"));

        Result<bool, Error> result = await ClientWith(handler)
            .IsOrgMemberAsync(INSTALLATION_ID, "dotnet-team", "octocat", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task All_requests_carry_token_authorization_and_user_agent()
    {
        HttpRequestMessage? captured = null;
        FakeHttpMessageHandler handler = new(req =>
        {
            captured = req;
            return Response(HttpStatusCode.OK, """{"id": 1}""");
        });

        await ClientWith(handler).GetUserIdByLoginAsync(INSTALLATION_ID, "u", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("token", captured!.Headers.Authorization?.Scheme);
        Assert.Equal(TOKEN, captured.Headers.Authorization?.Parameter);
        Assert.Contains(captured.Headers.UserAgent, ua => ua.Product?.Name == "sachkov-learn-onboarding");
        Assert.Contains(captured.Headers.Accept, mt => mt.MediaType == "application/vnd.github+json");
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
