using System.Net;
using System.Security.Cryptography;
using System.Text;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Core.Features.Webhooks.UseCases;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using SharedKernel;

namespace AssignmentReviewService.IntegrationTests.Features.Webhooks;

public sealed class HandleGitHubWebhookTests : AssignmentReviewServiceTestsBase
{
    private const long INSTALLATION_ID = 99001L;

    public HandleGitHubWebhookTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public void Webhook_has_bounded_request_body()
    {
        RouteEndpoint endpoint = Assert.Single(Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(),
            candidate => candidate.RoutePattern.RawText == "/assignment-review/webhooks/github");

        IRequestSizeLimitMetadata limit = Assert.IsAssignableFrom<IRequestSizeLimitMetadata>(
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>());
        Assert.Equal(HandleGitHubWebhookEndpoint.MAX_BODY_BYTES, limit.MaxRequestBodySize);
    }

    [Fact]
    public async Task Webhook_BadHmac_Returns401()
    {
        const string body = """{"action":"deleted","installation":{"id":99001}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('a', 64));
        req.Headers.Add("X-GitHub-Event", "installation");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_MissingSignature_Returns401()
    {
        const string body = """{"action":"deleted","installation":{"id":99001}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-GitHub-Event", "installation");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_InstallationDeleted_ValidHmac_MarksUninstalled()
    {
        // Arrange — pre-create installation в БД.
        VcsInstallation installation = VcsInstallation.Create(
            VcsProvider.GITHUB,
            INSTALLATION_ID.ToString(System.Globalization.CultureInfo.InvariantCulture),
            VcsInstallationOwnerType.USER,
            "test-user",
            "9999",
            DefaultUserId,
            RepoSelections.AllRepos());

        await ExecuteInDbAsync(async db =>
        {
            db.VcsInstallations.Add(installation);
            await db.SaveChangesAsync();
        });

        // Act — POST webhook installation/deleted с валидной HMAC.
        const string body = """{"action":"deleted","installation":{"id":99001}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(body, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", "installation");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string installationIdStr = INSTALLATION_ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ExecuteInDbAsync(async db =>
        {
            VcsInstallation? updated = await db.VcsInstallations
                .FirstOrDefaultAsync(i => i.InstallationId == installationIdStr);
            Assert.NotNull(updated);
            Assert.Equal(VcsInstallationStatus.UNINSTALLED, updated!.Status);
            Assert.NotNull(updated.RemovedAt);
        });
    }

    [Fact]
    public async Task Webhook_InstallationSuspended_ValidHmac_MarksSuspended()
    {
        VcsInstallation installation = VcsInstallation.Create(
            VcsProvider.GITHUB,
            INSTALLATION_ID.ToString(System.Globalization.CultureInfo.InvariantCulture),
            VcsInstallationOwnerType.USER,
            "test-user",
            "9999",
            DefaultUserId,
            RepoSelections.AllRepos());

        await ExecuteInDbAsync(async db =>
        {
            db.VcsInstallations.Add(installation);
            await db.SaveChangesAsync();
        });

        const string body = """{"action":"suspend","installation":{"id":99001}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(body, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", "installation");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string installationIdStr = INSTALLATION_ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ExecuteInDbAsync(async db =>
        {
            VcsInstallation? updated = await db.VcsInstallations
                .FirstOrDefaultAsync(i => i.InstallationId == installationIdStr);
            Assert.Equal(VcsInstallationStatus.SUSPENDED, updated!.Status);
        });
    }

    [Fact]
    public async Task Webhook_UnknownEvent_ValidHmac_Returns200_NoOp()
    {
        const string body = """{"action":"opened","pull_request":{"id":1}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(body, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", "pull_request");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        // Unknown event ignored — 200 OK without retry storm.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_TamperedBody_Returns401()
    {
        const string originalBody = """{"action":"deleted","installation":{"id":99001}}""";
        const string tamperedBody = """{"action":"deleted","installation":{"id":1234567}}""";

        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(tamperedBody, Encoding.UTF8, "application/json"),
        };
        // Подпись посчитана по originalBody — body отличается → 401.
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(originalBody, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", "installation");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── #451: installation.created recovery (install-callback не отработал) ────────

    [Fact]
    public async Task Webhook_InstallationCreated_NoExistingRow_ResolvableUser_RecoversInstallation()
    {
        // Arrange — строки НЕТ (callback упал на истёкшем state-token). AuthService
        // резолвит владельца GitHub-аккаунта в платформенного юзера.
        const long installationId = 99055L;
        Guid studentId = Guid.NewGuid();
        Factory.AuthClient.UserIdByGithubExternalIdHandler = _ => studentId;
        Factory.VcsProvider.InstallationDetailHandler = (_, _) =>
            Task.FromResult(Result.Success<VcsInstallationDetail, Error>(
                new VcsInstallationDetail(
                    "StudentLogin",
                    "778899",
                    VcsInstallationOwnerType.USER,
                    RepoSelections.Specific(["StudentLogin/DirectoryService"]))));

        const string body = """{"action":"created","installation":{"id":99055}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(body, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", "installation");

        // Act
        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        // Assert — установка создана, привязана к юзеру, ACTIVE, repos сохранены.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string installationIdStr = installationId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ExecuteInDbAsync(async db =>
        {
            VcsInstallation? created = await db.VcsInstallations
                .FirstOrDefaultAsync(i => i.InstallationId == installationIdStr);
            Assert.NotNull(created);
            Assert.Equal(studentId, created!.LinkedUserId);
            Assert.Equal(VcsInstallationStatus.ACTIVE, created.Status);
            Assert.Equal("studentlogin", created.OwnerLogin); // lowercase invariant
            Assert.Equal("778899", created.OwnerExternalId);
            Assert.False(created.RepoSelections.All);
            Assert.Contains("StudentLogin/DirectoryService", created.RepoSelections.Repos);
        });

        // #307: тот же event, что и callback — AccessService завершит onboarding step.
        VcsInstallationCreated published = OutboxCollector.OfType<VcsInstallationCreated>().Single();
        Assert.Equal(studentId, published.UserId);
        Assert.Equal(installationId, published.InstallationId);
    }

    [Fact]
    public async Task Webhook_InstallationCreated_NoExistingRow_UnresolvableUser_SkipsCreation()
    {
        // AuthService: GitHub-аккаунт не привязан к платформе (UserId=null) → не создаём строку.
        Factory.AuthClient.UserIdByGithubExternalIdHandler = _ => null;
        Factory.VcsProvider.InstallationDetailHandler = (_, _) =>
            Task.FromResult(Result.Success<VcsInstallationDetail, Error>(
                new VcsInstallationDetail(
                    "UnknownGuy", "555000", VcsInstallationOwnerType.USER, RepoSelections.AllRepos())));

        const string body = """{"action":"created","installation":{"id":99056}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(body, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", "installation");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            bool any = await db.VcsInstallations.AnyAsync(i => i.InstallationId == "99056");
            Assert.False(any);
        });
        Assert.Empty(OutboxCollector.OfType<VcsInstallationCreated>());
    }

    [Fact]
    public async Task Webhook_InstallationCreated_OrgOwner_SkipsRecovery()
    {
        // ORG-установка: даже если AuthService вернул бы юзера, recovery скипает по owner-type
        // (org external_id ≠ GitHub user id из user_logins → резолв всё равно был бы null).
        Factory.AuthClient.UserIdByGithubExternalIdHandler = _ => Guid.NewGuid();
        Factory.VcsProvider.InstallationDetailHandler = (_, _) =>
            Task.FromResult(Result.Success<VcsInstallationDetail, Error>(
                new VcsInstallationDetail(
                    "SomeOrg", "424242", VcsInstallationOwnerType.ORG, RepoSelections.AllRepos())));

        const string body = """{"action":"created","installation":{"id":99058}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(body, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", "installation");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            bool any = await db.VcsInstallations.AnyAsync(i => i.InstallationId == "99058");
            Assert.False(any);
        });
        Assert.Empty(OutboxCollector.OfType<VcsInstallationCreated>());
    }

    [Fact]
    public async Task Webhook_InstallationCreated_ExistingRow_ReactivatesWithoutDuplicate()
    {
        // Строка уже есть (UNINSTALLED) — created webhook должен Reactivate, не плодить дубль
        // и не идти в recovery-ветку.
        VcsInstallation existing = VcsInstallation.Create(
            VcsProvider.GITHUB, "99057", VcsInstallationOwnerType.USER,
            "test-user", "9999", DefaultUserId, RepoSelections.AllRepos());
        existing.MarkUninstalled(DateTimeOffset.UtcNow);
        await ExecuteInDbAsync(async db =>
        {
            db.VcsInstallations.Add(existing);
            await db.SaveChangesAsync();
        });

        const string body = """{"action":"created","installation":{"id":99057}}""";
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(body, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", "installation");

        HttpResponseMessage response = await AppHttpClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            List<VcsInstallation> rows = await db.VcsInstallations
                .Where(i => i.InstallationId == "99057")
                .ToListAsync();
            Assert.Single(rows);
            Assert.Equal(VcsInstallationStatus.ACTIVE, rows[0].Status);
        });
    }

    private static string ComputeSignature(string body, string secret)
    {
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}
