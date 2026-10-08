using Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

/// <summary>
///     S2S: <c>GET /internal/telegram/users/{userId}/plans/{planId}/membership</c> — проверяет,
///     состоит ли платформенный юзер хотя бы в одном из bound чатов плана прямо сейчас.
///     Используется AccessService для on-demand верификации членства. Резолвит UserLink по
///     PlatformUserId → telegram user id; для каждого <see cref="ChatBinding"/> плана зовёт
///     <see cref="ChatMembershipChecker.GetStatusAsync"/>. <c>isMember=true</c> если ANY чат
///     вернул <see cref="MembershipStatus.Member"/>.
/// </summary>
public sealed record CheckPlanMembershipQuery(Guid UserId, Guid PlanId) : IQuery;

public sealed class CheckPlanMembershipEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/telegram/users/{userId:guid}/plans/{planId:guid}/membership/",
                async Task<EndpointResult<PlanMembershipDto>> (
                    [FromRoute] Guid userId,
                    [FromRoute] Guid planId,
                    [FromServices] CheckPlanMembershipHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new CheckPlanMembershipQuery(userId, planId), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class CheckPlanMembershipHandler
    : IQueryHandlerWithResult<PlanMembershipDto, CheckPlanMembershipQuery>
{
    private const string STATUS_MEMBER = "member";
    private const string STATUS_NOT_MEMBER = "not_member";
    private const string STATUS_UNKNOWN = "unknown";

    private readonly IChatBindingRepository _bindings;
    private readonly IUserLinkRepository _userLinks;
    private readonly ChatMembershipChecker _membership;

    public CheckPlanMembershipHandler(
        IChatBindingRepository bindings,
        IUserLinkRepository userLinks,
        ChatMembershipChecker membership)
    {
        _bindings = bindings;
        _userLinks = userLinks;
        _membership = membership;
    }

    public async Task<Result<PlanMembershipDto, Error>> Handle(
        CheckPlanMembershipQuery query, CancellationToken ct)
    {
        // 1) UserLink → telegram user id. Нет привязки → статус unknown.
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.PlatformUserId == query.UserId, ct);
        if (linkResult.IsFailure)
            return new PlanMembershipDto(IsMember: false, Status: STATUS_UNKNOWN);

        long telegramUserId = linkResult.Value.TelegramUserId;

        // 2) Bound чаты плана (с EnrollmentGrantsMembership=true — те, доступ к которым
        //    дают grant'ы). Дедуп по TelegramChatId.
        IReadOnlyList<ChatBinding> bindings = await _bindings.GetManyByAsync(
            x => x.PlanId == query.PlanId && x.EnrollmentGrantsMembership, ct);

        long[] chatIds = bindings
            .Select(b => b.TelegramChatId)
            .Distinct()
            .ToArray();

        if (chatIds.Length == 0)
            return new PlanMembershipDto(IsMember: false, Status: STATUS_NOT_MEMBER);

        // 3) Параллельный membership-check; member если ЛЮБОЙ чат вернул Member.
        Task<MembershipStatus>[] statusTasks = chatIds
            .Select(chatId => _membership.GetStatusAsync(chatId, telegramUserId, ct))
            .ToArray();
        MembershipStatus[] statuses = await Task.WhenAll(statusTasks);

        if (statuses.Any(s => s == MembershipStatus.Member))
            return new PlanMembershipDto(IsMember: true, Status: STATUS_MEMBER);

        // Хоть один confirmed NotMember (и ни одного Member) → not_member.
        // Все Unknown (Telegram API не ответил по всем чатам) → unknown.
        bool anyConfirmed = statuses.Any(s => s == MembershipStatus.NotMember);
        return new PlanMembershipDto(
            IsMember: false,
            Status: anyConfirmed ? STATUS_NOT_MEMBER : STATUS_UNKNOWN);
    }
}
