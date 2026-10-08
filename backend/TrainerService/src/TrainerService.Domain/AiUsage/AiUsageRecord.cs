namespace TrainerService.Domain.AiUsage;

/// <summary>
/// Aggregate root: одна запись лоджера AI-использования (#614 C1) — кто, какую операцию, на какой
/// модели вызвал, сколько токенов потратил и во сколько (в микрорублях) это обошлось. Чистая запись
/// учёта (append-only): после создания не мутируется. Стоимость хранится в <b>микрорублях</b>
/// (₽ × 1 000 000) целым <see cref="long"/>, чтобы не терять копейки на дешёвых per-call вызовах при
/// округлении. <c>InputTokens</c>/<c>OutputTokens</c>/<c>TotalTokens</c> nullable — у STT без
/// token-биллинга (whisper-1) usage не приходит. C1 — только запись; никаких квот/лимитов (это C2).
/// </summary>
public sealed class AiUsageRecord
{
    private AiUsageRecord() { } // EF

    private AiUsageRecord(
        Guid id,
        Guid userId,
        AiUsageOperation operation,
        string model,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens,
        long costMicroRub,
        Guid? sessionId,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        Operation = operation;
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalTokens = totalTokens;
        CostMicroRub = costMicroRub;
        SessionId = sessionId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public AiUsageOperation Operation { get; private set; }

    public string Model { get; private set; } = string.Empty;

    public int? InputTokens { get; private set; }

    public int? OutputTokens { get; private set; }

    public int? TotalTokens { get; private set; }

    /// <summary>Стоимость вызова в микрорублях (₽ × 1 000 000). 0 — модель без прайсинга или STT без usage.</summary>
    public long CostMicroRub { get; private set; }

    /// <summary>Сессия-источник вызова (если применимо) — для разрезов учёта по сессии.</summary>
    public Guid? SessionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Фабрика записи учёта. Токены передаются примитивами (а не <c>Shared.AI.AiUsage</c>) — чтобы
    /// Domain не зависел от Core/Shared.AI; маппинг <c>AiUsage → (in,out,total)</c> делает Core
    /// (<c>AiUsageCostCalculator</c> / recorder). Любой токен может быть null (STT без биллинга).
    /// </summary>
    public static AiUsageRecord Create(
        Guid userId,
        AiUsageOperation operation,
        string model,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens,
        long costMicroRub,
        Guid? sessionId = null) =>
        new(
            Guid.CreateVersion7(),
            userId,
            operation,
            model ?? string.Empty,
            inputTokens,
            outputTokens,
            totalTokens,
            costMicroRub,
            sessionId,
            DateTimeOffset.UtcNow);
}
