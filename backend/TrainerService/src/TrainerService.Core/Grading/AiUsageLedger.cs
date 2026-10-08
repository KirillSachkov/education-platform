using Core.Database;
using Shared.AI;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.AiUsage;

namespace TrainerService.Core.Grading;

/// <summary>
///     Best-effort запись одного AI-вызова в лоджер использования (#614 C1) — токены + стоимость в
///     микрорублях + контекст пользователя/сессии. <b>Никогда не валит пользовательский запрос:</b>
///     запись учёта оборачивается в try/catch, ошибка только логируется (accounting — побочный
///     эффект, не источник правды запроса). Стейджит запись и делает СОБСТВЕННЫЙ
///     <c>SaveChangesAsync</c> — отдельно от транзакции, сохранившей ответ пользователя, чтобы сбой
///     учёта не откатил уже сохранённый ответ.
/// </summary>
public sealed class AiUsageLedger
{
    private readonly IAiUsageRepository _repository;
    private readonly AiUsageCostCalculator _calculator;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<AiUsageLedger> _logger;

    public AiUsageLedger(
        IAiUsageRepository repository,
        AiUsageCostCalculator calculator,
        ITransactionManager transactions,
        ILogger<AiUsageLedger> logger)
    {
        _repository = repository;
        _calculator = calculator;
        _transactions = transactions;
        _logger = logger;
    }

    /// <summary>
    ///     Пишет запись учёта одного LLM-вызова. <paramref name="usage"/> = null (вызова LLM не было
    ///     либо провайдер не вернул usage) → запись всё равно создаётся с нулевой стоимостью и
    ///     null-токенами (для аудита факта вызова). Стоимость считается калькулятором по модели.
    /// </summary>
    public Task RecordLlmAsync(
        Guid userId,
        AiUsageOperation operation,
        string model,
        AiUsage? usage,
        Guid? sessionId,
        CancellationToken ct) =>
        RecordAsync(
            userId,
            operation,
            model,
            usage?.InputTokens,
            usage?.OutputTokens,
            usage?.TotalTokens,
            _calculator.Cost(model, usage),
            sessionId,
            ct);

    /// <summary>
    ///     Пишет запись учёта STT-вызова (транскрипция). <c>AiTranscriptionResult</c> не несёт token-usage,
    ///     поэтому токены остаются null, а стоимость ОЦЕНИВАЕТСЯ по длительности аудио
    ///     (<paramref name="durationSeconds"/> = нормализованный provider duration) × per-minute-цене
    ///     (см. <see cref="AiUsageCostCalculator.TranscriptionCost"/>). gpt-4o-mini-transcribe token-billed,
    ///     но usage не доходит — per-minute это лучший доступный proxy (#614 C2). Длительность 0 → cost 0.
    /// </summary>
    public Task RecordTranscriptionAsync(
        Guid userId,
        string model,
        double durationSeconds,
        Guid? sessionId,
        CancellationToken ct) =>
        RecordAsync(
            userId,
            AiUsageOperation.TRANSCRIPTION,
            model,
            inputTokens: null,
            outputTokens: null,
            totalTokens: null,
            costMicroRub: _calculator.TranscriptionCost(durationSeconds),
            sessionId,
            ct);

    private async Task RecordAsync(
        Guid userId,
        AiUsageOperation operation,
        string model,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens,
        long costMicroRub,
        Guid? sessionId,
        CancellationToken ct)
    {
        try
        {
            AiUsageRecord record = AiUsageRecord.Create(
                userId, operation, model, inputTokens, outputTokens, totalTokens, costMicroRub, sessionId);

            await _repository.AddAsync(record, ct);

            UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
            if (save.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to persist AI-usage ledger row ({Operation}, model {Model}, user {UserId}): {Code}",
                    operation, model, userId, save.Error.Messages[0].Code);
            }
        }
#pragma warning disable CA1031 // accounting is best-effort — a ledger error must never fail the user's request
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(
                ex,
                "AI-usage ledger write threw ({Operation}, model {Model}, user {UserId}) — request unaffected.",
                operation, model, userId);
        }
    }
}
