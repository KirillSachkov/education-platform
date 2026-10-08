namespace AccessService.Core.Features.Billing.TBank;

/// <summary>
/// Маппинг T-Bank Status → нормализованный <c>PaymentWebhookRequest.Status</c>.
///
/// Используется как webhook adapter'ом (Phase F.1.2), так и reconciliation
/// service'ом (Phase F.1.4) — оба должны давать одинаковый результат.
/// </summary>
public static class TBankStatusMapper
{
    public static TBankStatusMapping Map(string tbankStatus, string? errorCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(tbankStatus);

        return tbankStatus switch
        {
            "NEW" or "FORM_SHOWED" or "AUTHORIZING" or "CONFIRMING"
                => new TBankStatusMapping("NOOP", null),

            "AUTHORIZED"
                => new TBankStatusMapping("AUTHORIZED", null),

            "CONFIRMED"
                => new TBankStatusMapping("PAID", null),

            "REJECTED"
                => new TBankStatusMapping("FAILED",
                    string.IsNullOrEmpty(errorCode)
                        ? "rejected"
                        : $"rejected (errorCode={errorCode})"),

            "REVERSED" => new TBankStatusMapping("FAILED", "reversed"),
            "DEADLINE_EXPIRED" => new TBankStatusMapping("FAILED", "deadline_expired"),
            "ATTEMPTS_EXPIRED" => new TBankStatusMapping("FAILED", "attempts_expired"),
            "CANCELED" => new TBankStatusMapping("FAILED", "canceled"),

            "REFUNDED" => new TBankStatusMapping("REFUNDED", "provider refund"),

            "PARTIAL_REFUNDED"
                => new TBankStatusMapping("NOOP", "partial_refunded_unsupported"),

            _ => throw new ArgumentException(
                $"Unknown T-Bank status: '{tbankStatus}'. Code update required.",
                nameof(tbankStatus)),
        };
    }
}

/// <summary>
/// Результат маппинга. <see cref="NormalizedStatus"/> — одно из:
/// "AUTHORIZED", "PAID", "FAILED", "REFUNDED", "NOOP".
/// </summary>
public sealed record TBankStatusMapping(string NormalizedStatus, string? Reason);
