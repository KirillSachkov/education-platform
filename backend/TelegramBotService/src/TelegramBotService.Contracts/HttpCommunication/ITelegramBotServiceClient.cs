using CSharpFunctionalExtensions;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;

namespace TelegramBotService.Contracts.HttpCommunication;

/// <summary>
///     Service-to-service HTTP-клиент к TelegramBotService для запросов о состоянии
///     chat-binding'ов плана. Используется AccessService при включении plan-onboarding,
///     чтобы ensure'ить TELEGRAM-шаг если у плана уже есть привязанный чат.
/// </summary>
public interface ITelegramBotServiceClient
{
    /// <summary>
    ///     Вызывает <c>GET /internal/telegram/plans/{planId}/has-active-chat-binding/</c>.
    ///     <c>true</c> = у плана есть хотя бы одна active <c>chat_bindings</c> запись.
    /// </summary>
    Task<Result<bool, Error>> HasActiveChatBindingAsync(
        Guid planId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Вызывает
    ///     <c>GET /internal/telegram/users/{userId}/plans/{planId}/membership/</c> —
    ///     состоит ли юзер хотя бы в одном bound чате плана. Используется AccessService
    ///     для on-demand верификации членства.
    /// </summary>
    Task<Result<PlanMembershipDto, Error>> CheckPlanMembershipAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken);
}
