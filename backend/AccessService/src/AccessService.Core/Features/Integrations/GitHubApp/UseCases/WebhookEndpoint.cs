using System.Text.Json;
using System.Text.Json.Serialization;
using AccessService.Core.Database;
using AccessService.Core.Diagnostics;
using AccessService.Domain.Integrations.GitHub;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Shared.GitHubApp;
using GitHubAppOptions = AccessService.Core.Features.Integrations.GitHubApp.GitHubAppOptions;

namespace AccessService.Core.Features.Integrations.GitHubApp.UseCases;

/// <summary>
///     Принимает webhook'и от GitHub App. Пути обрабатываемых event'ов:
///     <c>installation</c> (created, deleted, suspend, unsuspend) и
///     <c>organization</c> (member_added, member_removed, member_invited).
///     Authentication: HMAC-SHA256 подпись body против <c>WebhookSecret</c>.
/// </summary>
public sealed class GitHubWebhookEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/webhooks/github", async (
                HttpContext httpContext,
                [Microsoft.AspNetCore.Mvc.FromServices] GitHubWebhookHandler handler,
                CancellationToken ct) =>
            {
                using MemoryStream ms = new();
                await httpContext.Request.Body.CopyToAsync(ms, ct);
                byte[] body = ms.ToArray();

                string? signature = httpContext.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
                string? eventName = httpContext.Request.Headers["X-GitHub-Event"].FirstOrDefault();

                Microsoft.AspNetCore.Http.IResult result = await handler.HandleAsync(body, signature, eventName, ct);
                return result;
            })
            .AllowAnonymous()
            .RequireRateLimiting("github-webhook");
    }
}

public sealed class GitHubWebhookHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IAuthorGithubInstallationsRepository _installations;
    private readonly IGithubOrgInvitationsRepository _invitations;
    private readonly ITransactionManager _transactions;
    private readonly TimeProvider _time;
    private readonly GitHubAppOptions _options;
    private readonly OnboardingMetrics _metrics;
    private readonly ILogger<GitHubWebhookHandler> _logger;

    public GitHubWebhookHandler(
        IAuthorGithubInstallationsRepository installations,
        IGithubOrgInvitationsRepository invitations,
        ITransactionManager transactions,
        TimeProvider time,
        IOptions<GitHubAppOptions> options,
        OnboardingMetrics metrics,
        ILogger<GitHubWebhookHandler> logger)
    {
        _installations = installations;
        _invitations = invitations;
        _transactions = transactions;
        _time = time;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Microsoft.AspNetCore.Http.IResult> HandleAsync(byte[] body, string? signature, string? eventName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            _logger.LogError("GitHubApp:WebhookSecret not configured — rejecting all webhooks");
            return Results.StatusCode(503);
        }

        if (string.IsNullOrEmpty(signature))
        {
            _metrics.RecordWebhookSignature("missing");
            return Results.Unauthorized();
        }

        if (!WebhookSignatureVerifier.Verify(signature, body, _options.WebhookSecret))
        {
            _metrics.RecordWebhookSignature("invalid");
            _logger.LogWarning(
                "GitHub webhook signature mismatch (event={Event}, sig_preview={Preview})",
                eventName, WebhookSignatureVerifier.PreviewSignature(signature));
            return Results.Unauthorized();
        }

        _metrics.RecordWebhookSignature("valid");

        if (string.IsNullOrEmpty(eventName))
        {
            return Results.BadRequest();
        }

        try
        {
            UnitResult<Error> handlingResult = UnitResult.Success<Error>();
            switch (eventName)
            {
                case "installation":
                    handlingResult = await HandleInstallationEventAsync(body, ct);
                    break;
                case "organization":
                    handlingResult = await HandleOrganizationEventAsync(body, ct);
                    break;
                default:
                    _metrics.RecordWebhookEvent(eventName, "ignored");
                    // Ignored event — return 200 to не лочить retry.
                    break;
            }

            if (handlingResult.IsFailure)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed webhook payload (event={Event})", eventName);
            return Results.BadRequest();
        }
    }

    private async Task<UnitResult<Error>> HandleInstallationEventAsync(byte[] body, CancellationToken ct)
    {
        InstallationEventPayload? payload = JsonSerializer.Deserialize<InstallationEventPayload>(body, JsonOpts);
        if (payload?.Installation is null) return UnitResult.Success<Error>();

        AuthorGithubInstallation? installation =
            await _installations.GetByInstallationIdAsync(payload.Installation.Id, ct);
        if (installation is null) return UnitResult.Success<Error>(); // installation не наш — игнорим

        DateTimeOffset now = _time.GetUtcNow();
        switch (payload.Action)
        {
            case "deleted":
            case "suspend":
                installation.Suspend(now);
                break;
            case "unsuspend":
            case "created":
                installation.Unsuspend();
                break;
        }
        _metrics.RecordWebhookEvent("installation", payload.Action);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Installation event {Action} → installation_id={InstallationId} save failed: {Code}",
                payload.Action, installation.InstallationId, saveResult.Error.Messages[0].Code);
            return saveResult.Error;
        }
        _logger.LogInformation(
            "Installation event {Action} → installation_id={InstallationId} suspended={Suspended}",
            payload.Action, installation.InstallationId, !installation.IsActive);
        return UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> HandleOrganizationEventAsync(byte[] body, CancellationToken ct)
    {
        OrganizationEventPayload? payload = JsonSerializer.Deserialize<OrganizationEventPayload>(body, JsonOpts);
        if (payload?.Organization is null || payload.Membership?.User is null)
        {
            return UnitResult.Success<Error>();
        }

        _metrics.RecordWebhookEvent("organization", payload.Action);

        // Интересуют только member_added / member_invited (другие events игнорим — invitation
        // expire/revoke ловятся через manual sync либо GitHub-side timeouts).
        if (!string.Equals(payload.Action, "member_added", StringComparison.Ordinal))
        {
            return UnitResult.Success<Error>();
        }

        string orgLogin = payload.Organization.Login.ToLowerInvariant();
        string githubLogin = payload.Membership.User.Login.ToLowerInvariant();

        GithubOrgInvitation? invitation =
            await _invitations.GetPendingByOrgLoginAsync(orgLogin, githubLogin, ct);
        if (invitation is null)
        {
            _logger.LogInformation(
                "GitHub member_added: no pending invitation for {Login} in {Org} — ignored",
                githubLogin, orgLogin);
            return UnitResult.Success<Error>();
        }

        invitation.MarkAccepted(_time.GetUtcNow());

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "GitHub invitation accept save failed: invitation_id={Id} login={Login} org={Org} code={Code}",
                invitation.Id, githubLogin, orgLogin, saveResult.Error.Messages[0].Code);
            return saveResult.Error;
        }

        _logger.LogInformation(
            "GitHub invitation accepted via webhook: invitation_id={Id} login={Login} org={Org}",
            invitation.Id, githubLogin, orgLogin);
        return UnitResult.Success<Error>();
    }

    private sealed class InstallationEventPayload
    {
        [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("installation")] public InstallationField? Installation { get; set; }
    }

    private sealed class InstallationField
    {
        [JsonPropertyName("id")] public long Id { get; set; }
    }

    private sealed class OrganizationEventPayload
    {
        [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("organization")] public OrgField? Organization { get; set; }
        [JsonPropertyName("membership")] public MembershipField? Membership { get; set; }
    }

    private sealed class OrgField
    {
        [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
    }

    private sealed class MembershipField
    {
        [JsonPropertyName("user")] public UserField? User { get; set; }
    }

    private sealed class UserField
    {
        [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
    }
}
