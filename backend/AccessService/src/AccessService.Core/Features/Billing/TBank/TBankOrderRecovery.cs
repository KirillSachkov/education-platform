using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Domain;

namespace AccessService.Core.Features.Billing.TBank;

public static class TBankOrderRecovery
{
    public static Result<TBankPaymentHistory, Error> MatchAndAttach(
        Order order,
        TBankCheckOrderResponse response)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(response);

        string expectedOrderId = order.Id.ToString();
        if (!string.Equals(response.OrderId, expectedOrderId, StringComparison.Ordinal))
        {
            return Error.Failure(
                "tbank.check_order.order_id_mismatch",
                "CheckOrder вернул другой OrderId");
        }

        if (response.Payments is null || response.Payments.Count == 0)
        {
            return Error.Failure(
                "tbank.check_order.empty",
                "CheckOrder не вернул платежей для заказа");
        }

        if (response.Payments.Count != 1)
        {
            return Error.Failure(
                "tbank.check_order.ambiguous",
                "CheckOrder вернул не единственный платёж для заказа");
        }

        TBankPaymentHistory payment = response.Payments[0];
        if (payment.Amount != order.AmountCents
            || string.IsNullOrWhiteSpace(payment.PaymentId)
            || string.IsNullOrWhiteSpace(payment.Status))
        {
            return Error.Failure(
                "tbank.check_order.no_exact_match",
                "CheckOrder не вернул платёж с ожидаемой суммой");
        }
        UnitResult<Error> attach = order.AttachExternalRef(payment.PaymentId);
        if (attach.IsFailure)
        {
            return Error.Failure(
                "tbank.check_order.payment_id_conflict",
                "Локальный PaymentId конфликтует с CheckOrder");
        }

        return payment;
    }
}
