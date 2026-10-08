using Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Preferences.Dtos;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.Preferences.UseCases;

/// <summary>
/// GET /notifications/preferences — per-user флаги каналов + per-type opt-outs.
/// Если записи нет — отдаём дефолты из <see cref="UserNotificationChannels.Default"/> (Email ON, Telegram ON)
/// + пустой opt-out список (подписан на всё).
/// </summary>
public sealed class GetMyPreferencesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/preferences", async Task<EndpointResult<NotificationPreferenceDto>> (
                [FromServices] GetMyPreferencesHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new GetMyPreferencesQuery(), cancellationToken))
            .RequireAuthorization();
    }
}

public sealed record GetMyPreferencesQuery : IQuery;

public sealed class GetMyPreferencesHandler
    : IQueryHandlerWithResult<NotificationPreferenceDto, GetMyPreferencesQuery>
{
    private readonly IUserChannelsRepository _userChannels;
    private readonly IUserOptOutsRepository _userOptOuts;
    private readonly UserScopedData _user;

    public GetMyPreferencesHandler(
        IUserChannelsRepository userChannels,
        IUserOptOutsRepository userOptOuts,
        UserScopedData user)
    {
        _userChannels = userChannels;
        _userOptOuts = userOptOuts;
        _user = user;
    }

    public async Task<Result<NotificationPreferenceDto, Error>> Handle(
        GetMyPreferencesQuery query,
        CancellationToken cancellationToken)
    {
        UserNotificationChannels? row = await _userChannels.GetByUserIdAsync(_user.UserId, cancellationToken);
        UserNotificationChannels effective = row ?? UserNotificationChannels.Default(_user.UserId);

        IReadOnlySet<NotificationType> optedOut =
            await _userOptOuts.GetOptedOutTypesAsync(_user.UserId, cancellationToken);
        List<short> optedOutShorts = new(optedOut.Count);
        foreach (NotificationType t in optedOut)
            optedOutShorts.Add((short)t);
        optedOutShorts.Sort();

        return new NotificationPreferenceDto
        {
            TelegramEnabled = effective.TelegramEnabled,
            EmailEnabled = effective.EmailEnabled,
            WebPushEnabled = effective.WebPushEnabled,
            OptedOutTypes = optedOutShorts,
        };
    }
}
