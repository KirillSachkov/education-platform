using AuthService.Core.Database;
using AuthService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Users.Queries;

/// <summary>
///     DTO одной записи о согласии для UI «мои согласия» в Settings → Privacy.
/// </summary>
public sealed record ConsentRecordDto(
    string ConsentType,
    string DocumentVersion,
    DateTime AcceptedAt);

public sealed record GetMyConsentsResponse(IReadOnlyList<ConsentRecordDto> Consents);

public sealed record GetMyConsentsQuery : IQuery;

public sealed class GetMyConsentsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/me/consents", async Task<EndpointResult<GetMyConsentsResponse>> (
                    [Microsoft.AspNetCore.Mvc.FromServices] GetMyConsentsHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetMyConsentsQuery(), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
}

public sealed class GetMyConsentsHandler : IQueryHandlerWithResult<GetMyConsentsResponse, GetMyConsentsQuery>
{
    private readonly IConsentRepository _consents;
    private readonly UserScopedData _user;

    public GetMyConsentsHandler(IConsentRepository consents, UserScopedData user)
    {
        _consents = consents;
        _user = user;
    }

    public async Task<Result<GetMyConsentsResponse, Error>> Handle(
        GetMyConsentsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<UserConsent> records = await _consents.GetByUserAsync(_user.UserId, cancellationToken);

        var dtos = records
            .Select(c => new ConsentRecordDto(c.ConsentType.ToString(), c.DocumentVersion, c.AcceptedAt))
            .ToList();

        return new GetMyConsentsResponse(dtos);
    }
}
