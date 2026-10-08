using AccessService.Core.Features.Billing.Configuration;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Domain;
using Microsoft.Extensions.Options;

namespace AccessService.Core.Features.Billing.TBank;

/// <summary>
/// Формирует <see cref="TBankReceipt"/> для CloudKassir-интегрированного T-Bank
/// эквайринга (54-ФЗ). Items[0] = одна услуга «Доступ к плану X».
///
/// Если у юзера нет email — fallback на <see cref="TBankReceiptOptions.DefaultEmail"/>;
/// чек попадёт на fallback-email (юзер не получит, но flow не падает).
/// </summary>
public sealed class TBankReceiptBuilder
{
    private const int RECEIPT_NAME_MAX_LENGTH = 128;

    private readonly TBankReceiptOptions _options;

    public TBankReceiptBuilder(IOptions<TBankOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Receipt;
    }

    public TBankReceipt Build(Plan plan, string? userEmail, long amountCents)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string email = string.IsNullOrWhiteSpace(userEmail) ? _options.DefaultEmail : userEmail;

        return new TBankReceipt
        {
            Email = email,
            Taxation = _options.Taxation,
            Items =
            [
                new TBankReceiptItem
                {
                    Name = TruncateForReceipt($"Доступ к плану «{plan.DisplayName.Value}»"),
                    Price = amountCents,
                    Quantity = 1,
                    Amount = amountCents,
                    Tax = "none",                      // ИП на УСН без НДС
                    PaymentMethod = "full_payment",
                    PaymentObject = "service",
                },
            ],
        };
    }

    // T-Bank ограничивает Name до 128 символов (требование ФНС).
    private static string TruncateForReceipt(string name) =>
        name.Length <= RECEIPT_NAME_MAX_LENGTH ? name : name[..RECEIPT_NAME_MAX_LENGTH];
}
