using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

/// <summary>
///     <c>GET /telegram/me/chats</c> — возвращает все TG-чаты, к которым у текущего юзера
///     есть доступ через активный <c>PlanGrant</c> + binding с
///     <c>EnrollmentGrantsMembership=true</c>. Под капотом: вытащить активные plan-grants
///     юзера → найти bound chats для этих планов → вернуть invite link'и + метадату.
/// </summary>
public sealed record GetMyChatsQuery : IQuery;

public sealed class GetMyChatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/telegram/me/chats/",
                async Task<EndpointResult<IReadOnlyList<MyChatBindingDto>>> (
                    [FromServices] GetMyChatsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMyChatsQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class GetMyChatsHandler : IQueryHandlerWithResult<IReadOnlyList<MyChatBindingDto>, GetMyChatsQuery>
{
    private readonly IChatBindingRepository _bindings;
    private readonly IUserLinkRepository _userLinks;
    private readonly IAccessServiceClient _accessClient;
    private readonly ChatMembershipChecker _membership;
    private readonly UserScopedData _user;
    private readonly ILogger<GetMyChatsHandler> _logger;

    public GetMyChatsHandler(
        IChatBindingRepository bindings,
        IUserLinkRepository userLinks,
        IAccessServiceClient accessClient,
        ChatMembershipChecker membership,
        UserScopedData user,
        ILogger<GetMyChatsHandler> logger)
    {
        _bindings = bindings;
        _userLinks = userLinks;
        _accessClient = accessClient;
        _membership = membership;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<MyChatBindingDto>, Error>> Handle(
        GetMyChatsQuery query, CancellationToken cancellationToken)
    {
        if (_user.UserId == Guid.Empty)
            return Result.Success<IReadOnlyList<MyChatBindingDto>, Error>([]);

        // 1) Активные plan-grants юзера через AccessService.
        Result<IReadOnlyList<PlanGrantDto>, Error> grantsResult =
            await _accessClient.GetUserGrantsAsync(_user.UserId, cancellationToken);
        if (grantsResult.IsFailure)
        {
            _logger.LogWarning(
                "AccessService GetUserGrants failed for user {UserId}: {Code}",
                _user.UserId, FirstErrorCode(grantsResult.Error));
            return grantsResult.Error;
        }

        Result<IReadOnlyDictionary<Guid, PlanGrantDto>, Error> accessResult =
            await TelegramGrantChatAccessResolver.ResolveAsync(
                grantsResult.Value,
                _accessClient,
                cancellationToken);
        if (accessResult.IsFailure)
            return accessResult.Error;

        IReadOnlyDictionary<Guid, PlanGrantDto> accessByBindingPlanId = accessResult.Value;
        IReadOnlySet<Guid> activePlanIds = accessByBindingPlanId.Keys.ToHashSet();

        if (activePlanIds.Count == 0)
            return Result.Success<IReadOnlyList<MyChatBindingDto>, Error>([]);

        // 2) Bindings для этих планов с EnrollmentGrantsMembership=true.
        IReadOnlyList<ChatBinding> accessibleBindings = await _bindings.GetManyByAsync(
            x => x.EnrollmentGrantsMembership && activePlanIds.Contains(x.PlanId),
            cancellationToken);

        if (accessibleBindings.Count == 0)
            return Result.Success<IReadOnlyList<MyChatBindingDto>, Error>([]);

        // 3) Дедуп по TelegramChatId.
        var groupedByChat = accessibleBindings
            .GroupBy(b => b.TelegramChatId)
            .ToList();

        // 4) Резолвим TG-id юзера для membership-check.
        long? telegramUserId = null;
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.PlatformUserId == _user.UserId, cancellationToken);
        if (linkResult.IsSuccess)
            telegramUserId = linkResult.Value.TelegramUserId;

        // 5) Параллельный membership-check.
        bool[] memberFlags;
        if (telegramUserId is { } tgId)
        {
            Task<MembershipStatus>[] statusTasks = groupedByChat
                .Select(g => _membership.GetStatusAsync(g.Key, tgId, cancellationToken))
                .ToArray();
            MembershipStatus[] statuses = await Task.WhenAll(statusTasks);
            memberFlags = statuses.Select(s => s == MembershipStatus.Member).ToArray();
        }
        else
        {
            memberFlags = new bool[groupedByChat.Count];
        }

        // 6) Резолвим plan display-names из enriched grant DTO (если populated).
        Dictionary<Guid, string> titleByPlanId = accessByBindingPlanId
            .Where(pair => pair.Value.Plan is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Plan!.DisplayName);

        // 7) Mapping.
        var result = new List<MyChatBindingDto>(groupedByChat.Count);
        for (int i = 0; i < groupedByChat.Count; i++)
        {
            IGrouping<long, ChatBinding> group = groupedByChat[i];
            ChatBinding first = group.First();
            IReadOnlyList<Guid> chatPlanIds = group
                .Select(b => b.PlanId)
                .Distinct()
                .ToList();
            IReadOnlyList<string> chatPlanTitles = chatPlanIds
                .Select(id => titleByPlanId.GetValueOrDefault(id, string.Empty))
                .ToList();

            result.Add(new MyChatBindingDto(
                chatPlanIds,
                chatPlanTitles,
                first.TelegramChatId,
                first.ChatType.ToString(),
                first.ChatTitle,
                first.InviteLink,
                memberFlags[i]));
        }

        return Result.Success<IReadOnlyList<MyChatBindingDto>, Error>(result);
    }

    private static string FirstErrorCode(Error error) =>
        error.Messages is { Count: > 0 } messages ? messages[0].Code : "unknown";
}
