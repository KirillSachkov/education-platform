namespace AccessService.Contracts.Billing;

/// <summary>
/// Response для <c>POST /access/orders/</c>. <c>PaymentUrl</c> — куда редиректить
/// юзера на форму T-Bank.
/// </summary>
public sealed record CreateOrderResponse(Guid OrderId, string PaymentUrl);
