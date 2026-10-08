using AccessService.Domain;

namespace AccessService.Core.Domain;

/// <summary>
/// Durable per-user state machine for <c>POST /access/orders/</c>.
///
/// Phase F.1.0 — введён в issue #102.
/// </summary>
public sealed class IdempotencyKey
{
    public const int KEY_MAX_LENGTH = 64;
    public const int RESPONSE_MAX_LENGTH = 2000;

    private IdempotencyKey() { } // EF

    private IdempotencyKey(
        string key,
        Guid userId,
        Guid planId,
        PlanScope expectedScope,
        Guid orderId,
        DateTimeOffset createdAt)
    {
        Key = key;
        UserId = userId;
        PlanId = planId;
        ExpectedScope = expectedScope;
        Status = IdempotencyKeyStatus.PROCESSING;
        OrderId = orderId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string Key { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public Guid PlanId { get; private set; }

    public PlanScope ExpectedScope { get; private set; }

    public IdempotencyKeyStatus Status { get; private set; }

    public Guid OrderId { get; private set; }

    public string? ResponseBody { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static IdempotencyKey CreateProcessing(
        string key,
        Guid userId,
        Guid planId,
        PlanScope expectedScope,
        Guid orderId) => new(
            key,
            userId,
            planId,
            expectedScope,
            orderId,
            DateTimeOffset.UtcNow);

    public UnitResult<Error> Complete(string responseBody)
    {
        if (Status != IdempotencyKeyStatus.PROCESSING)
            return Error.Conflict("idempotency.status.not_processing", "Операция больше не выполняется");
        if (string.IsNullOrWhiteSpace(responseBody) || responseBody.Length > RESPONSE_MAX_LENGTH)
            return Error.Validation("idempotency.response.invalid", "Некорректный результат идемпотентной операции");

        ResponseBody = responseBody;
        Status = IdempotencyKeyStatus.COMPLETED;
        UpdatedAt = DateTimeOffset.UtcNow;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Fail()
    {
        if (Status != IdempotencyKeyStatus.PROCESSING)
            return Error.Conflict("idempotency.status.not_processing", "Операция больше не выполняется");

        Status = IdempotencyKeyStatus.FAILED;
        UpdatedAt = DateTimeOffset.UtcNow;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> RecoverTerminal()
    {
        if (Status != IdempotencyKeyStatus.PROCESSING)
            return Error.Conflict("idempotency.status.not_processing", "Операция больше не выполняется");

        Status = IdempotencyKeyStatus.RECOVERED;
        UpdatedAt = DateTimeOffset.UtcNow;
        return UnitResult.Success<Error>();
    }
}

public enum IdempotencyKeyStatus
{
    PROCESSING,
    COMPLETED,
    FAILED,
    RECOVERED,
}
