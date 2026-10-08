using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccessService.Core.Features.Billing.TBank.Contracts;

public sealed class TBankCheckOrderResponse
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

    [JsonPropertyName("OrderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("Payments")]
    public IReadOnlyList<TBankPaymentHistory> Payments { get; set; } = null!;
}

public sealed class TBankPaymentHistory
{
    [JsonPropertyName("PaymentId")]
    public string PaymentId { get; set; } = string.Empty;

    [JsonPropertyName("Amount")]
    public long? Amount { get; set; }

    [JsonPropertyName("Status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("Success")]
    [JsonConverter(typeof(TBankFlexibleBooleanConverter))]
    public bool Success { get; set; }

    [JsonPropertyName("ErrorCode")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}

public sealed class TBankCard
{
    [JsonPropertyName("CardId")]
    public string CardId { get; set; } = string.Empty;

    [JsonPropertyName("Pan")]
    public string? Pan { get; set; }

    [JsonPropertyName("Status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("RebillId")]
    public string? RebillId { get; set; }

    [JsonPropertyName("CardType")]
    public int? CardType { get; set; }

    [JsonPropertyName("ExpDate")]
    public string? ExpDate { get; set; }
}

internal sealed class TBankFlexibleBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out bool value) => value,
            _ => throw new JsonException("Expected boolean or boolean string"),
        };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
        writer.WriteBooleanValue(value);
}
