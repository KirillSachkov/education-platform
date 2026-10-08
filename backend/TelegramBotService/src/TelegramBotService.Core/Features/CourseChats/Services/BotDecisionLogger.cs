using Core.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.Audit;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
///     Тонкая обёртка вокруг <see cref="IBotDecisionRepository"/>: пишет аудит-запись
///     отдельной мини-транзакцией и проглатывает исключения (логирует warning, не валит
///     вызывающий handler). Аудит — диагностический, не должен ломать business-flow.
///
///     Использует свой собственный scope (<see cref="IServiceScopeFactory"/>) для
///     изоляции <see cref="BotDecision"/>-INSERT'а от ambient EF-context'а вызывающего
///     handler'а — иначе SaveChanges флашит чужие staged изменения вместе с audit-row'ом.
/// </summary>
public sealed class BotDecisionLogger : IBotDecisionLogger
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BotDecisionLogger> _logger;

    public BotDecisionLogger(
        IServiceScopeFactory scopeFactory,
        ILogger<BotDecisionLogger> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task LogAsync(
        long telegramChatId,
        long telegramUserId,
        string decision,
        string? reason = null,
        Guid? planId = null,
        CancellationToken ct = default)
    {
        try
        {
            string? trimmedReason = reason is { Length: > BotDecision.REASON_MAX_LENGTH }
                ? reason[..BotDecision.REASON_MAX_LENGTH]
                : reason;

            string trimmedDecision = decision.Length > BotDecision.DECISION_MAX_LENGTH
                ? decision[..BotDecision.DECISION_MAX_LENGTH]
                : decision;

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IBotDecisionRepository repo = scope.ServiceProvider
                .GetRequiredService<IBotDecisionRepository>();
            ITransactionManager transactions = scope.ServiceProvider
                .GetRequiredService<ITransactionManager>();

            await repo.AddAsync(
                new BotDecision(
                    Guid.CreateVersion7(),
                    telegramChatId,
                    telegramUserId,
                    trimmedDecision,
                    trimmedReason,
                    planId,
                    DateTime.UtcNow),
                ct);
            await transactions.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record bot decision: chat={ChatId} user={UserId} decision={Decision}",
                telegramChatId, telegramUserId, decision);
        }
    }
}
