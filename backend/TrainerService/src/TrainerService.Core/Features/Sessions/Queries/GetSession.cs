using ContentAccess;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.Queries;

public sealed record GetSessionQuery(Guid SessionId, Guid UserId, bool IsAdmin) : IQuery;

public sealed class GetSessionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/sessions/{sessionId:guid}",
                async Task<EndpointResult<SessionDto>> (
                    Guid sessionId,
                    GetSessionHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetSessionQuery(sessionId, user.UserId, user.IsAdmin), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Возвращает сессию вызывающего (resume/review). Items без ключа грейдинга; для уже
///     отвеченных item'ов SessionMapper раскрывает вердикт/фидбэк/разбор + правильный ответ.
///     Чужую сессию вернуть нельзя (scoped по UserId) — 404.
/// </summary>
public sealed class GetSessionHandler : IQueryHandlerWithResult<SessionDto, GetSessionQuery>
{
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IEntitlementChecker _entitlements;

    public GetSessionHandler(ITrainingSessionsRepository sessions, IEntitlementChecker entitlements)
    {
        _sessions = sessions;
        _entitlements = entitlements;
    }

    public async Task<Result<SessionDto, Error>> Handle(
        GetSessionQuery query,
        CancellationToken cancellationToken)
    {
        Result<TrainingSession, Error> sessionResult = await _sessions.GetWithItemsAsync(
            s => s.Id == query.SessionId && s.UserId == query.UserId,
            cancellationToken);
        if (sessionResult.IsFailure)
            return TrainerServiceErrors.Session.NotFound(query.SessionId);

        // PRO-статус нужен для per-item замка OPEN_TEXT при resume/review (#623).
        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            query.UserId, query.IsAdmin, _entitlements, cancellationToken);

        return SessionMapper.ToDto(sessionResult.Value, hasPro);
    }
}
