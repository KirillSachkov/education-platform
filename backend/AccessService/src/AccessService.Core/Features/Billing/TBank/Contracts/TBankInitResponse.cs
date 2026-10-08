using System.Text.Json.Serialization;

namespace AccessService.Core.Features.Billing.TBank.Contracts;

public sealed class TBankInitResponse
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("ErrorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }

    [JsonPropertyName("Details")]
    public string? Details { get; set; }

    [JsonPropertyName("TerminalKey")]
    public string? TerminalKey { get; set; }

    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    [JsonPropertyName("PaymentId")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("OrderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("Amount")]
    public long? Amount { get; set; }

    [JsonPropertyName("PaymentURL")]
    public string? PaymentURL { get; set; }
}
