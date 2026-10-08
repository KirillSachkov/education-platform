using System.Text.Json.Serialization;

namespace AccessService.Core.Features.Billing.TBank.Contracts;

/// <summary>
/// Incoming webhook payload от T-Bank. ⚠️ <c>PaymentId</c> в JSON — число, парсим в long.
/// </summary>
public sealed class TBankNotification
{
    [JsonPropertyName("TerminalKey")]
    public string TerminalKey { get; set; } = string.Empty;

    [JsonPropertyName("OrderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("PaymentId")]
    public long PaymentId { get; set; }

    [JsonPropertyName("ErrorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("Amount")]
    public long Amount { get; set; }

    [JsonPropertyName("Pan")]
    public string? Pan { get; set; }

    [JsonPropertyName("ExpDate")]
    public string? ExpDate { get; set; }

    [JsonPropertyName("RebillId")]
    public string? RebillId { get; set; }

    [JsonPropertyName("Token")]
    public string Token { get; set; } = string.Empty;
}
