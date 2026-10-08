using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.Onboarding;

/// <summary>
///     <c>GET /telegram/me/onboarding-status?planId=...</c> — frontend onboarding
///     wizard читает этот endpoint в TG-шаге. Возвращает: привязан ли Telegram,
///     username юзера, список tg-чатов плана с join-URL'ами.
/// </summary>
public sealed class GetMyPlanOnboardingStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/telegram/me/onboarding-status/",
                async Task<EndpointResult<PlanOnboardingStatusResponse>> (
                    [FromQuery(Name = "planId")] Guid planId,
                    [FromServices] GetMyPlanOnboardingStatusHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetMyPlanOnboardingStatusQuery(planId), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed record GetMyPlanOnboardingStatusQuery(Guid PlanId) : ICommand;

public sealed class GetMyPlanOnboardingStatusHandler
    : ICommandHandler<PlanOnboardingStatusResponse, GetMyPlanOnboardingStatusQuery>
{
    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _chatBindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly UserScopedData _user;

    public GetMyPlanOnboardingStatusHandler(
        IUserLinkRepository userLinks,
        IChatBindingRepository chatBindings,
        IAccessServiceClient accessClient,
        UserScopedData user)
    {
        _userLinks = userLinks;
        _chatBindings = chatBindings;
        _accessClient = accessClient;
        _user = user;
    }

    public async Task<Result<PlanOnboardingStatusResponse, Error>> Handle(
        GetMyPlanOnboardingStatusQuery query, CancellationToken ct)
    {
        Result<IReadOnlyList<PlanGrantDto>, Error> grantsResult =
            await _accessClient.GetUserGrantsAsync(_user.UserId, ct);
        if (grantsResult.IsFailure)
            return grantsResult.Error;

        PlanGrantDto[] matchingGrants = grantsResult.Value
            .Where(grant => grant.PlanId == query.PlanId)
            .ToArray();
        Result<IReadOnlyDictionary<Guid, PlanGrantDto>, Error> accessResult =
            await TelegramGrantChatAccessResolver.ResolveAsync(matchingGrants, _accessClient, ct);
        if (accessResult.IsFailure)
            return accessResult.Error;

        if (accessResult.Value.Count == 0)
        {
            return Error.Authorization(
                "telegram.plan.access_denied",
                "Нет активного доступа к этому плану");
        }

        Guid bindingPlanId = accessResult.Value.Keys.Single();

        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            l => l.PlatformUserId == _user.UserId, ct);

        bool isLinked = linkResult.IsSuccess;
        string? telegramUsername = isLinked ? linkResult.Value.TelegramUsername : null;

        IReadOnlyList<ChatBinding> bindings = await _chatBindings.GetManyByAsync(
            b => b.PlanId == bindingPlanId && b.EnrollmentGrantsMembership, ct);

        IReadOnlyList<PlanChatDto> chats = bindings
            .Select(b => new PlanChatDto(
                b.TelegramChatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b.ChatTitle,
                b.InviteLink,
                b.ChatType.ToString(),
                b.EnrollmentGrantsMembership))
            .ToList();

        return new PlanOnboardingStatusResponse(isLinked, telegramUsername, chats);
    }
}
