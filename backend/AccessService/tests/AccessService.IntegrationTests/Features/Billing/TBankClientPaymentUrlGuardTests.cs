using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using AccessService.Core.Features.Billing.Configuration;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

/// <summary>
/// Regression for the 2026-06-03 production payment incident(s).
///
/// Round 1 (#430): the open-redirect guard in <see cref="TBankClient"/> compared the returned
/// <c>PaymentURL</c> host against the API <c>BaseUrl</c> host (<c>securepay.tinkoff.ru</c>), but
/// T-Bank hosts the payment FORM on a different sub-domain — every real payment was rejected with
/// <c>tbank.response.invalid</c> ("unexpected payment URL host") and the buy button failed.
///
/// Round 2 (#440): the first fix replaced that with a fixed allow-list of <c>securepay*</c> hosts,
/// but the production terminal actually returns the form on the short-link host <c>pay.tbank.ru</c>
/// (T-Bank rebrand), which was NOT in the list — so the buy button kept failing with the same error.
///
/// The guard now trusts the T-Bank apex domains (<c>tinkoff.ru</c> / <c>tbank.ru</c>) and any of
/// their sub-domains, which is robust against T-Bank moving the form between hosts while still
/// rejecting foreign / look-alike hosts. Incident #1170 additionally permits the exact
/// pay.tbank-online.com host returned by the production terminal (2026-09-28). These tests exercise the real
/// <see cref="TBankClient.InitAsync"/> against a stubbed HTTP transport — the integration suite's
/// <c>FakeTBankClient</c> bypasses the guard entirely, which is why CI stayed green while prod
/// payments were down.
/// </summary>
public sealed class TBankClientPaymentUrlGuardTests
{
    [Theory]
    [InlineData("https://PAY.TBANK-ONLINE.COM/diagnostic")]
    [InlineData("https://pay.tbank-online.com/diagnostic")] // prod terminal, incident #1170
    [InlineData("https://pay.tbank.ru/XNGrOatM")]           // REAL prod host (#440) — short-link form
    [InlineData("https://pay.tinkoff.ru/abc")]              // pre-rebrand short-link host
    [InlineData("https://securepayments.tinkoff.ru/x/abc")] // legacy payment-form host
    [InlineData("https://securepay.tinkoff.ru/new/abc")]    // API host is also legitimate
    [InlineData("https://securepayments.tbank.ru/x/abc")]   // rebrand payment-form host
    public async Task InitAsync_accepts_legitimate_tbank_payment_hosts(string paymentUrl)
    {
        Result<TBankInitResponse, Error> result = await RunInitAsync(InitOkJson(paymentUrl));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.GetMessage() : null);
        Assert.Equal(paymentUrl, result.Value.PaymentURL);
    }

    [Theory]
    [InlineData("http://pay.tbank-online.com/diagnostic")]
    [InlineData("https://pay.tbank-online.com.evil.example/diagnostic")]
    [InlineData("https://evilpay.tbank-online.com/diagnostic")]
    [InlineData("https://sub.pay.tbank-online.com/diagnostic")]
    [InlineData("https://tbank-online.com/diagnostic")]
    [InlineData("https://pay.tbank-online.com@evil.example/diagnostic")]
    [InlineData("https://evil.example.com/x/abc")]               // foreign host — open redirect
    [InlineData("http://pay.tbank.ru/x/abc")]                    // non-https
    [InlineData("https://securepayments.tinkoff.ru.evil.com/x")] // look-alike suffix (extra label)
    [InlineData("https://eviltinkoff.ru/x")]                     // look-alike apex (no dot separator)
    [InlineData("https://tbank.ru.attacker.io/x")]               // trusted apex as a left label
    public async Task InitAsync_rejects_untrusted_or_insecure_payment_url(string paymentUrl)
    {
        Result<TBankInitResponse, Error> result = await RunInitAsync(InitOkJson(paymentUrl));

        Assert.True(result.IsFailure);
        Assert.Equal("tbank.response.untrusted_host", result.Error.Messages[0].Code);
    }

    private static async Task<Result<TBankInitResponse, Error>> RunInitAsync(
        string responseJson,
        string baseUrl = "https://securepay.tinkoff.ru/v2/")
    {
        using var handler = new StubHandler(responseJson);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        using ServiceProvider sp = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new PaymentMetrics(sp.GetRequiredService<IMeterFactory>());
        var options = Options.Create(new TBankOptions
        {
            TerminalKey = "test-terminal",
            Password = "test-password",
            BaseUrl = baseUrl,
        });

        var client = new TBankClient(http, options, metrics, NullLogger<TBankClient>.Instance);
        return await client.InitAsync(new TBankInitRequest
        {
            Amount = 100000,
            OrderId = "order-1",
            Description = "test",
        });
    }

    private static string InitOkJson(string paymentUrl) =>
        "{\"Success\":true,\"ErrorCode\":\"0\",\"Status\":\"NEW\",\"PaymentId\":\"1234567890\","
        + "\"OrderId\":\"order-1\",\"Amount\":100000,\"PaymentURL\":\""
        + paymentUrl
        + "\",\"TerminalKey\":\"test-terminal\"}";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
    }
}
