using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
///     Один-call хелпер: проверяет права бота в чате и обновляет
///     <see cref="ChatBinding.IsHealthy"/> + <see cref="ChatBinding.LastValidationError"/>.
///     Используется как periodic'ом (<see cref="ChatBindingHealthCheckService"/>),
///     так и реактивно при <c>my_chat_member</c> событиях (см. <see cref="ChatMember"/> handlers).
/// </summary>
public sealed class ChatBindingHealthService
{
    private readonly IChatBindingRepository _bindings;
    private readonly IChatAdministrationApi _chatApi;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<ChatBindingHealthService> _logger;

    public ChatBindingHealthService(
        IChatBindingRepository bindings,
        IChatAdministrationApi chatApi,
        ITransactionManager transactions,
        ILogger<ChatBindingHealthService> logger)
    {
        _bindings = bindings;
        _chatApi = chatApi;
        _transactions = transactions;
        _logger = logger;
    }

    /// <summary>
    ///     Проверяет права бота в чате одного binding'а. Если права в порядке —
    ///     <see cref="ChatBinding.MarkHealthy"/>; иначе MarkUnhealthy с описанием.
    ///     Сохраняет изменения. Возвращает true если binding после проверки здоров.
    /// </summary>
    public async Task<bool> ValidateAsync(ChatBinding binding, CancellationToken ct)
    {
        var permResult = await _chatApi.GetBotPermissionsAsync(binding.TelegramChatId, ct);

        string? newError = null;
        if (permResult.IsFailure)
        {
            newError = $"chat_unreachable:{permResult.ErrorCode}";
        }
        else
        {
            var p = permResult.Value!;
            if (!p.IsAdministrator)
                newError = "bot_not_administrator";
            else if (!p.CanInviteUsers)
                newError = "missing_can_invite_users";
            else if (!p.CanRestrictMembers && binding.AutoKickOnRevoke)
                newError = "missing_can_restrict_members";
        }

        bool wasHealthy = binding.IsHealthy;
        if (newError is null)
        {
            binding.MarkHealthy();
        }
        else
        {
            binding.MarkUnhealthy(newError);
        }

        // GetManyByAsync возвращает untracked entities — explicit Update для записи изменений.
        _bindings.Update(binding);
        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            throw saveResult.Error.AsTransient().ToException();

        if (wasHealthy && newError is not null)
        {
            _logger.LogWarning(
                "ChatBinding {BindingId} (chat {ChatId}, course {CourseId}) marked unhealthy: {Reason}",
                binding.Id, binding.TelegramChatId, binding.PlanId, newError);
        }
        else if (!wasHealthy && newError is null)
        {
            _logger.LogInformation(
                "ChatBinding {BindingId} (chat {ChatId}, course {CourseId}) recovered to healthy",
                binding.Id, binding.TelegramChatId, binding.PlanId);
        }

        return newError is null;
    }

    /// <summary>
    ///     Marks unhealthy without making API call — используется реактивно из MyChatMember handler
    ///     когда мы УЖЕ знаем что бот kicked / not-admin (Telegram сам прислал событие).
    /// </summary>
    public async Task MarkUnhealthyAsync(long telegramChatId, string reason, CancellationToken ct)
    {
        IReadOnlyList<ChatBinding> bindings = await _bindings.GetManyByAsync(
            x => x.TelegramChatId == telegramChatId, ct);

        if (bindings.Count == 0)
            return;

        int changed = 0;
        foreach (ChatBinding binding in bindings)
        {
            if (binding.IsHealthy || !string.Equals(binding.LastValidationError, reason, StringComparison.Ordinal))
            {
                binding.MarkUnhealthy(reason);
                _bindings.Update(binding);
                changed++;
            }
        }

        if (changed > 0)
        {
            UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
            if (saveResult.IsFailure)
                throw saveResult.Error.AsTransient().ToException();
            _logger.LogWarning(
                "Marked {Changed}/{Total} chat_bindings for chat {ChatId} unhealthy: {Reason}",
                changed, bindings.Count, telegramChatId, reason);
        }
    }
}
