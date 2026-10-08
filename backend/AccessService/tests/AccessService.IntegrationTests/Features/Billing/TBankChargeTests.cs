using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
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
/// Unit-level coverage of <see cref="TBankClient.ChargeAsync"/> (#614, A2a) — the recurring
/// child-charge call used by the A2b sweeper. Drives the real client against a stubbed HTTP
/// transport (the integration suite's <c>FakeTBankClient</c> bypasses token signing entirely,
/// so the token scheme must be exercised here against <see cref="TBankClient"/> directly).
/// </summary>
public sealed class TBankChargeTests
{
    private const string TERMINAL = "test-terminal";
    private const string PASSWORD = "test-password";

    [Fact]
    public async Task ChargeAsync_signs_token_over_terminal_payment_rebill_and_password()
    {
        // Token = sha256 over {TerminalKey, PaymentId, RebillId} + (Password) sorted Ordinal.
        const string paymentId = "9876543210";
        const string rebillId = "rebill-xyz";

        var captured = new CapturingHandler(ChargeOkJson(paymentId));
        TBankChargeResponse body = (await RunChargeAsync(captured, paymentId, rebillId)).Value;

        Assert.Equal("CONFIRMED", body.Status);
        Assert.Equal(paymentId, body.PaymentId);

        // The client must POST to the Charge operation.
        Assert.NotNull(captured.RequestUri);
        Assert.EndsWith("Charge", captured.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        // Verify the request shape + token recomputed independently from the same scheme.
        using JsonDocument doc = JsonDocument.Parse(captured.RequestBody!);
        JsonElement root = doc.RootElement;
        Assert.Equal(TERMINAL, root.GetProperty("TerminalKey").GetString());
        Assert.Equal(paymentId, root.GetProperty("PaymentId").GetString());
        Assert.Equal(rebillId, root.GetProperty("RebillId").GetString());

        string sentToken = root.GetProperty("Token").GetString()!;
        string expectedToken = TBankSignature.ComputeToken(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TerminalKey"] = TERMINAL,
                ["PaymentId"] = paymentId,
                ["RebillId"] = rebillId,
            },
            PASSWORD);
        Assert.Equal(expectedToken, sentToken);

        // RebillId participates in the hash — a different RebillId yields a different token.
        string otherToken = TBankSignature.ComputeToken(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TerminalKey"] = TERMINAL,
                ["PaymentId"] = paymentId,
                ["RebillId"] = "other-rebill",
            },
            PASSWORD);
        Assert.NotEqual(expectedToken, otherToken);
    }

    [Fact]
    public async Task ChargeAsync_returns_failure_when_provider_reports_unsuccessful()
    {
        var captured = new CapturingHandler(
            "{\"Success\":false,\"Status\":\"REJECTED\",\"PaymentId\":\"1\",\"ErrorCode\":\"1051\",\"Message\":\"Недостаточно средств\"}");

        Result<TBankChargeResponse, Error> result = await RunChargeAsync(captured, "1", "rebill");

        Assert.True(result.IsFailure);
        Assert.Equal("tbank.charge.failed", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task ChargeAsync_returns_network_error_on_non_success_http()
    {
        var captured = new CapturingHandler("{}", HttpStatusCode.BadGateway);

        Result<TBankChargeResponse, Error> result = await RunChargeAsync(captured, "1", "rebill");

        Assert.True(result.IsFailure);
        Assert.Equal("tbank.network.error", result.Error.Messages[0].Code);
    }

    private static async Task<Result<TBankChargeResponse, Error>> RunChargeAsync(
        CapturingHandler handler,
        string paymentId,
        string rebillId)
    {
        const string baseUrl = "https://securepay.tinkoff.ru/v2/";
        using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        using ServiceProvider sp = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new PaymentMetrics(sp.GetRequiredService<IMeterFactory>());
        var options = Options.Create(new TBankOptions
        {
            TerminalKey = TERMINAL,
            Password = PASSWORD,
            BaseUrl = baseUrl,
        });

        var client = new TBankClient(http, options, metrics, NullLogger<TBankClient>.Instance);
        return await client.ChargeAsync(paymentId, rebillId);
    }

    private static string ChargeOkJson(string paymentId) =>
        "{\"Success\":true,\"ErrorCode\":\"0\",\"Status\":\"CONFIRMED\",\"PaymentId\":\""
        + paymentId
        + "\"}";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;

        public CapturingHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _json = json;
            _status = status;
        }

        public string? RequestBody { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
