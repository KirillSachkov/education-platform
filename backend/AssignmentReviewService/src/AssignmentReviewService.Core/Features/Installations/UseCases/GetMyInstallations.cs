using AssignmentReviewService.Contracts.Installations;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Vcs;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AssignmentReviewService.Core.Features.Installations.UseCases;

/// <summary>
///     Возвращает список GitHub App installation'ов текущего юзера. Используется
///     карточкой «AI-проверка PR'ов» в <c>/settings/integrations</c> — фронт
///     рендерит «Подключить» если список пуст, или список подключённых аккаунтов /
///     организаций + кнопку «Управлять» (ведёт на GitHub Settings).
///     <para>
///         Для admin'а возвращает installation'ы admin-юзера, не глобально (это
///         user-scoped resource — список «своих» подключений). Глобальный список
///         для admin'ов планируется отдельно через <c>/admin/installations</c>
///         (не реализован — не в scope MVP).
///     </para>
/// </summary>
public sealed record GetMyInstallationsQuery : IQuery;

public sealed class GetMyInstallationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/assignment-review/installations/me/",
                async Task<EndpointResult<GetMyInstallationsResponse>> (
                    [FromServices] GetMyInstallationsHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetMyInstallationsQuery(), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

public sealed class GetMyInstallationsHandler
    : IQueryHandlerWithResult<GetMyInstallationsResponse, GetMyInstallationsQuery>
{
    private readonly IVcsInstallationsRepository _installations;
    private readonly UserScopedData _user;

    public GetMyInstallationsHandler(IVcsInstallationsRepository installations, UserScopedData user)
    {
        _installations = installations;
        _user = user;
    }

    public async Task<Result<GetMyInstallationsResponse, Error>> Handle(
        GetMyInstallationsQuery query, CancellationToken ct)
    {
        Guid userId = _user.UserId;

        IReadOnlyList<VcsInstallation> installations =
            await _installations.GetManyByAsync(i => i.LinkedUserId == userId, ct);

        IReadOnlyList<VcsInstallationDto> dtos = installations
            .Select(i => new VcsInstallationDto
            {
                Id = i.Id,
                Provider = i.Provider.ToString(),
                OwnerLogin = i.OwnerLogin,
                OwnerType = i.OwnerType.ToString(),
                Status = i.Status.ToString(),
                AllRepos = i.RepoSelections.All,
                Repos = i.RepoSelections.Repos,
                InstalledAt = i.InstalledAt,
                RemovedAt = i.RemovedAt,
            })
            .ToList();

        return new GetMyInstallationsResponse { Installations = dtos };
    }
}
