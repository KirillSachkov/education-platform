using System.Text.Json.Serialization;

namespace AccessService.Core.Features.Billing.TBank.Contracts;

/// <summary>
/// Ответ T-Bank на <c>Charge</c> (#614) — безредиректное автосписание по сохранённому
/// <c>RebillId</c>. На успех <c>Success=true</c> + <c>Status=CONFIRMED</c> (одностадийный).
/// </summary>
public sealed class TBankChargeResponse
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("PaymentId")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("ErrorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}
