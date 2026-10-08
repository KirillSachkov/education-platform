using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using AccessService.Core.Features.Billing.Configuration;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessService.IntegrationTests.Features.Billing;

public sealed class TBankInitProtocolTests
{
    private const string Terminal = "test-terminal";
    private const string Password = "test-password";

    [Fact]
    public async Task InitAsync_RecurrentParent_IncludesRecurrentInToken()
    {
        const string baseUrl = "https://securepay.tinkoff.ru/v2/";
        using var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new PaymentMetrics(services.GetRequiredService<IMeterFactory>());
        var options = Options.Create(new TBankOptions
        {
            TerminalKey = Terminal,
            Password = Password,
            BaseUrl = baseUrl,
        });
        var client = new TBankClient(http, options, metrics, NullLogger<TBankClient>.Instance);

        await client.InitAsync(new TBankInitRequest
        {
            Amount = 99_000,
            OrderId = "order-1",
            Recurrent = "Y",
            CustomerKey = "customer-1",
            DATA = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OperationInitiatorType"] = "1",
            },
        });

        using JsonDocument doc = JsonDocument.Parse(handler.RequestBody!);
        JsonElement root = doc.RootElement;
        Assert.Equal("Y", root.GetProperty("Recurrent").GetString());
        Assert.Equal("customer-1", root.GetProperty("CustomerKey").GetString());
        Assert.False(root.TryGetProperty("OperationInitiatorType", out _));
        Assert.Equal("1", root.GetProperty("DATA").GetProperty("OperationInitiatorType").GetString());
        string expected = TBankSignature.ComputeToken(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TerminalKey"] = Terminal,
                ["Amount"] = "99000",
                ["OrderId"] = "order-1",
                ["Recurrent"] = "Y",
                ["CustomerKey"] = "customer-1",
            },
            Password);

        Assert.Equal(expected, root.GetProperty("Token").GetString());
    }

    [Fact]
    public void InitRequest_DoesNotExposeTopLevelOperationInitiatorType()
    {
        Assert.Null(typeof(TBankInitRequest).GetProperty("OperationInitiatorType"));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"Success\":true,\"ErrorCode\":\"0\",\"Status\":\"NEW\","
                    + "\"PaymentId\":\"1234567890\",\"OrderId\":\"order-1\",\"Amount\":99000,"
                    + "\"PaymentURL\":\"https://pay.tbank.ru/payment\",\"TerminalKey\":\"test-terminal\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
