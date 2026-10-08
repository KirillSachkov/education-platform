using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AccessService.Core.Features.Billing.TBank.Contracts;

[SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "JSON DTO — mutable for System.Text.Json deserialization.")]
[SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "JSON DTO — List<T> required by T-Bank protocol shape.")]
public sealed class TBankInitRequest
{
    public const int DESCRIPTION_MAX_LENGTH = 140;

    [JsonPropertyName("TerminalKey")]
    public string TerminalKey { get; set; } = string.Empty;

    [JsonPropertyName("Amount")]
    public long Amount { get; set; }

    [JsonPropertyName("OrderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("NotificationURL")]
    public string? NotificationURL { get; set; }

    [JsonPropertyName("SuccessURL")]
    public string? SuccessURL { get; set; }

    [JsonPropertyName("FailURL")]
    public string? FailURL { get; set; }

    [JsonPropertyName("PayType")]
    public string? PayType { get; set; }

    /// <summary>
    /// Признак родительского рекуррентного платежа (#614): <c>"Y"</c> для первого платежа
    /// подписки — T-Bank вернёт <c>RebillId</c> в webhook'е AUTHORIZED, который мы сохраняем
    /// до CONFIRMED для безредиректных автосписаний. <c>null</c> для разовых платежей.
    /// </summary>
    [JsonPropertyName("Recurrent")]
    public string? Recurrent { get; set; }

    /// <summary>
    /// Стабильный идентификатор покупателя у T-Bank (#614). Обязателен вместе с
    /// <see cref="Recurrent"/>="Y" — по нему привязывается сохранённая карта. Используем строку
    /// userId. <c>null</c> для разовых платежей.
    /// </summary>
    [JsonPropertyName("CustomerKey")]
    public string? CustomerKey { get; set; }

    [JsonPropertyName("DATA")]
    public Dictionary<string, string>? DATA { get; set; }

    [JsonPropertyName("Receipt")]
    public TBankReceipt? Receipt { get; set; }

    [JsonPropertyName("Token")]
    public string Token { get; set; } = string.Empty;
}

[SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "JSON DTO — mutable for System.Text.Json deserialization.")]
[SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "JSON DTO — List<T> required by T-Bank protocol shape.")]
public sealed class TBankReceipt
{
    [JsonPropertyName("Email")]
    public string? Email { get; set; }

    [JsonPropertyName("Phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("Taxation")]
    public string Taxation { get; set; } = "usn_income";

    [JsonPropertyName("Items")]
    public List<TBankReceiptItem> Items { get; set; } = new();
}

public sealed class TBankReceiptItem
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Price")]
    public long Price { get; set; }

    [JsonPropertyName("Quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("Amount")]
    public long Amount { get; set; }

    [JsonPropertyName("Tax")]
    public string Tax { get; set; } = "none";

    [JsonPropertyName("PaymentMethod")]
    public string PaymentMethod { get; set; } = "full_payment";

    [JsonPropertyName("PaymentObject")]
    public string PaymentObject { get; set; } = "service";
}
