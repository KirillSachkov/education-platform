using AccessService.Core.Features.Integrations.GitHubApp.Services;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Infrastructure;

/// <summary>
///     Управляемый фейк GitHub App API для onboarding-тестов (#501). Реальный
///     <c>GitHubAppApiClient</c> ходит в api.github.com через installation-token —
///     в integration-тестах подменяется этим фейком (паттерн FakeAuthServiceClient).
/// </summary>
public sealed class FakeGitHubAppApiClient : IGitHubAppApiClient
{
    /// <summary>Логины (case-insensitive), считающиеся active-member'ами любой org.</summary>
    public HashSet<string> OrgMembers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Результат POST /orgs/{org}/invitations.</summary>
    public CreateInvitationResult CreateResult { get; set; } = new CreateInvitationResult.Created(777);

    /// <summary>Numeric id, который вернёт GET /users/{login}; null → ошибка lookup'а.</summary>
    public long? UserIdByLogin { get; set; } = 1001;

    /// <summary>true → IsOrgMemberAsync возвращает ошибку (GitHub недоступен).</summary>
    public bool MembershipCheckFails { get; set; }

    public int CreateOrgInvitationCalls { get; private set; }

    public int MembershipChecks { get; private set; }

    private readonly List<RemoveOrgMemberCall> _removeOrgMemberCalls = [];

    /// <summary>
    ///     Записанные вызовы <see cref="RemoveOrgMemberAsync"/> — (installationId, orgLogin,
    ///     username). #687: тесты проверяют, что истечение/отзыв доступа исключает юзера из org'и.
    /// </summary>
    public IReadOnlyList<RemoveOrgMemberCall> RemoveOrgMemberCalls => _removeOrgMemberCalls;

    /// <summary>Результат, который вернёт <see cref="RemoveOrgMemberAsync"/> (по умолчанию success/true).</summary>
    public Result<bool, Error> RemoveOrgMemberResult { get; set; } = Result.Success<bool, Error>(true);

    public Task<Result<InstallationDetail, Error>> GetInstallationAsync(
        long installationId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success<InstallationDetail, Error>(
            new InstallationDetail("sachkovtech", "Organization")));

    public Task<Result<long, Error>> GetUserIdByLoginAsync(
        long installationId, string login, CancellationToken ct = default) =>
        Task.FromResult(UserIdByLogin is { } id
            ? Result.Success<long, Error>(id)
            : Result.Failure<long, Error>(Error.NotFound(
                "github_app.user.not.found", "GitHub-пользователь не найден")));

    public Task<CreateInvitationResult> CreateOrgInvitationAsync(
        long installationId, string orgLogin, long inviteeId, CancellationToken ct = default)
    {
        CreateOrgInvitationCalls++;
        return Task.FromResult(CreateResult);
    }

    public Task<Result<bool, Error>> IsOrgMemberAsync(
        long installationId, string orgLogin, string username, CancellationToken ct = default)
    {
        MembershipChecks++;
        return Task.FromResult(MembershipCheckFails
            ? Result.Failure<bool, Error>(Error.Failure(
                "github_app.api.unavailable", "GitHub API недоступен"))
            : Result.Success<bool, Error>(OrgMembers.Contains(username)));
    }

    public Task<Result<bool, Error>> RemoveOrgMemberAsync(
        long installationId, string orgLogin, string username, CancellationToken ct = default)
    {
        _removeOrgMemberCalls.Add(new RemoveOrgMemberCall(installationId, orgLogin, username));
        return Task.FromResult(RemoveOrgMemberResult);
    }

    /// <summary>Вернуть фейк к дефолтам — тесты вызывают в arrange, чтобы не зависеть от соседей.</summary>
    public void Reset()
    {
        OrgMembers.Clear();
        CreateResult = new CreateInvitationResult.Created(777);
        UserIdByLogin = 1001;
        MembershipCheckFails = false;
        CreateOrgInvitationCalls = 0;
        MembershipChecks = 0;
        _removeOrgMemberCalls.Clear();
        RemoveOrgMemberResult = Result.Success<bool, Error>(true);
    }
}

/// <summary>Зафиксированный вызов DELETE /orgs/{org}/memberships/{username} (#687).</summary>
public sealed record RemoveOrgMemberCall(long InstallationId, string OrgLogin, string Username);
