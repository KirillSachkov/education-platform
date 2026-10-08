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
///     <c>POST /telegram/admin/users/{userId}/plans/{planId}/welcome/resend/</c> — support-action
///     «переотправить приветствие плана» произвольному пользователю. Резолвит <see cref="UserLink"/>
///     (нет → <c>NotLinked</c>), ищет bound-чат плана с <c>EnrollmentGrantsMembership=true</c>
///     (нет → <c>NoChatBound</c>), затем зовёт <see cref="PlanWelcomeService.TrySendWelcomeAsync"/>
///     с <c>force:true</c> (минует dedup) и мапит <see cref="WelcomeOutcome"/> в строку. Issue #444.
/// </summary>
public sealed record ResendPlanWelcomeCommand(Guid UserId, Guid PlanId) : ICommand;

public sealed class ResendPlanWelcomeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/telegram/admin/users/{userId:guid}/plans/{planId:guid}/welcome/resend/",
                async Task<EndpointResult<ResendWelcomeResponse>> (
                    [FromRoute] Guid userId,
                    [FromRoute] Guid planId,
                    [FromServices] ResendPlanWelcomeHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new ResendPlanWelcomeCommand(userId, planId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR)
            .RequireRateLimiting("telegram-link");
    }
}

public sealed class ResendPlanWelcomeHandler
    : ICommandHandler<ResendWelcomeResponse, ResendPlanWelcomeCommand>
{
    private const string OUTCOME_NOT_LINKED = "NotLinked";
    private const string OUTCOME_NO_CHAT_BOUND = "NoChatBound";

    private readonly IUserLinkRepository _userLinks;
    private readonly IChatBindingRepository _bindings;
    private readonly PlanWelcomeService _planWelcome;
    private readonly UserScopedData _user;
    private readonly ILogger<ResendPlanWelcomeHandler> _logger;

    public ResendPlanWelcomeHandler(
        IUserLinkRepository userLinks,
        IChatBindingRepository bindings,
        PlanWelcomeService planWelcome,
        UserScopedData user,
        ILogger<ResendPlanWelcomeHandler> logger)
    {
        _userLinks = userLinks;
        _bindings = bindings;
        _planWelcome = planWelcome;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<ResendWelcomeResponse, Error>> Handle(
        ResendPlanWelcomeCommand command, CancellationToken ct)
    {
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.PlatformUserId == command.UserId, ct);

        if (linkResult.IsFailure)
        {
            LogAction(command, OUTCOME_NOT_LINKED);
            return new ResendWelcomeResponse(OUTCOME_NOT_LINKED);
        }

        UserLink link = linkResult.Value;

        // Bound-чат плана, членство в котором даётся за enrollment — туда (в личку) шлём welcome.
        Guid planId = command.PlanId;
        Result<ChatBinding, Error> bindingResult = await _bindings.GetByAsync(
            x => x.PlanId == planId && x.EnrollmentGrantsMembership, ct);

        if (bindingResult.IsFailure)
        {
            LogAction(command, OUTCOME_NO_CHAT_BOUND);
            return new ResendWelcomeResponse(OUTCOME_NO_CHAT_BOUND);
        }

        WelcomeOutcome outcome = await _planWelcome.TrySendWelcomeAsync(
            command.PlanId,
            link.TelegramUserId,
            bindingResult.Value.TelegramChatId,
            WelcomeDestination.DirectMessage,
            ct,
            force: true);

        string outcomeName = outcome.ToString();
        LogAction(command, outcomeName);
        return new ResendWelcomeResponse(outcomeName);
    }

    private void LogAction(ResendPlanWelcomeCommand command, string outcome) =>
        _logger.LogInformation(
            "support-action {Action} admin={AdminUserId} target={TargetUserId} plan={PlanId} outcome={Outcome}",
            "welcome.resend", _user.UserId, command.UserId, command.PlanId, outcome);
}
