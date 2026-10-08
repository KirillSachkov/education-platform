using System.Diagnostics.CodeAnalysis;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory фейк для интеграционных тестов. Не делает HTTP-вызовов.
/// Каждый метод можно настроить через <c>...Handler</c> в тесте.
/// </summary>
[SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Test infrastructure — call recorders need concrete List for direct test inspection.")]
[SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Test infrastructure — call recorders are intentionally mutable for tests.")]
public sealed class FakeTBankClient : ITBankClient
{
    public Func<TBankInitRequest, Result<TBankInitResponse, Error>> InitHandler { get; set; } =
        req => new TBankInitResponse
        {
            Success = true,
            ErrorCode = "0",
            Status = "NEW",
            PaymentId = "1234567890",
            OrderId = req.OrderId,
            Amount = req.Amount,
            PaymentURL = $"https://securepayments.tinkoff.ru/x/{req.OrderId}",
            TerminalKey = req.TerminalKey,
        };

    public Func<string, Result<TBankGetStateResponse, Error>> GetStateHandler { get; set; } =
        paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "NEW",
            PaymentId = paymentId,
        };

    public Func<string, Result<TBankCheckOrderResponse, Error>> CheckOrderHandler { get; set; } =
        orderId => new TBankCheckOrderResponse
        {
            Success = true,
            OrderId = orderId,
            Payments = [],
        };

    public Func<string, Result<IReadOnlyList<TBankCard>, Error>> GetCardListHandler { get; set; } =
        _ => Array.Empty<TBankCard>();

    public Func<string, Result<TBankGetStateResponse, Error>> CancelHandler { get; set; } =
        paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CANCELED",
            PaymentId = paymentId,
        };

    public Func<(string PaymentId, string RebillId), Result<TBankChargeResponse, Error>> ChargeHandler { get; set; } =
        args => new TBankChargeResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = args.PaymentId,
        };

    public List<TBankInitRequest> InitCalls { get; } = new();

    public List<string> GetStateCalls { get; } = new();

    public List<string> CheckOrderCalls { get; } = new();

    public List<string> GetCardListCalls { get; } = new();

    public List<string> CancelCalls { get; } = new();

    public List<(string PaymentId, string RebillId)> ChargeCalls { get; } = new();

    /// <summary>
    /// Сбрасывает recorded calls + восстанавливает default handlers. Вызывается
    /// между тестами в <c>AccessServiceTestsBase.InitializeAsync</c>, чтобы
    /// singleton-фейк не утекал состояние между независимыми кейсами.
    /// </summary>
    public void Reset()
    {
        InitCalls.Clear();
        GetStateCalls.Clear();
        CheckOrderCalls.Clear();
        GetCardListCalls.Clear();
        CancelCalls.Clear();
        ChargeCalls.Clear();

        InitHandler = req => new TBankInitResponse
        {
            Success = true,
            ErrorCode = "0",
            Status = "NEW",
            PaymentId = "1234567890",
            OrderId = req.OrderId,
            Amount = req.Amount,
            PaymentURL = $"https://securepayments.tinkoff.ru/x/{req.OrderId}",
            TerminalKey = req.TerminalKey,
        };

        GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "NEW",
            PaymentId = paymentId,
        };

        CheckOrderHandler = orderId => new TBankCheckOrderResponse
        {
            Success = true,
            OrderId = orderId,
            Payments = [],
        };

        GetCardListHandler = _ => Array.Empty<TBankCard>();

        CancelHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CANCELED",
            PaymentId = paymentId,
        };

        ChargeHandler = args => new TBankChargeResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = args.PaymentId,
        };
    }

    public Task<Result<TBankInitResponse, Error>> InitAsync(TBankInitRequest request, CancellationToken ct = default)
    {
        InitCalls.Add(request);
        return Task.FromResult(InitHandler(request));
    }

    public Task<Result<TBankGetStateResponse, Error>> GetStateAsync(string paymentId, CancellationToken ct = default)
    {
        GetStateCalls.Add(paymentId);
        return Task.FromResult(GetStateHandler(paymentId));
    }

    public Task<Result<TBankCheckOrderResponse, Error>> CheckOrderAsync(
        string orderId,
        CancellationToken ct = default)
    {
        CheckOrderCalls.Add(orderId);
        return Task.FromResult(CheckOrderHandler(orderId));
    }

    public Task<Result<IReadOnlyList<TBankCard>, Error>> GetCardListAsync(
        string customerKey,
        CancellationToken ct = default)
    {
        GetCardListCalls.Add(customerKey);
        return Task.FromResult(GetCardListHandler(customerKey));
    }

    public Task<Result<TBankGetStateResponse, Error>> CancelAsync(string paymentId, CancellationToken ct = default)
    {
        CancelCalls.Add(paymentId);
        return Task.FromResult(CancelHandler(paymentId));
    }

    public Task<Result<TBankChargeResponse, Error>> ChargeAsync(string paymentId, string rebillId, CancellationToken ct = default)
    {
        ChargeCalls.Add((paymentId, rebillId));
        return Task.FromResult(ChargeHandler((paymentId, rebillId)));
    }
}
