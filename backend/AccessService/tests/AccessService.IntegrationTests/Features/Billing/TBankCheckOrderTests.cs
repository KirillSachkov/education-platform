using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using AccessService.Core.Features.Billing.Configuration;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessService.IntegrationTests.Features.Billing;

public sealed class TBankCheckOrderTests
{
    private const string TERMINAL = "test-terminal";
    private const string PASSWORD = "test-password";

    [Fact]
    public async Task CheckOrderAsync_SignsOrderIdAndDeserializesFullHistory()
    {
        var handler = new CapturingHandler(
            """
            {
              "Success": true,
              "ErrorCode": "0",
              "Message": "OK",
              "TerminalKey": "test-terminal",
              "OrderId": "order-747",
              "Payments": [
                {
                  "PaymentId": "payment-1",
                  "Amount": 99000,
                  "Status": "CONFIRMED",
                  "Success": "true",
                  "ErrorCode": 0,
                  "Message": "OK"
                }
              ]
            }
            """);

        TBankCheckOrderResponse result = (await CreateClient(handler)
            .CheckOrderAsync("order-747")).Value;

        Assert.EndsWith("CheckOrder", handler.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(handler.RequestBody!);
        JsonElement request = document.RootElement;
        Assert.Equal(TERMINAL, request.GetProperty("TerminalKey").GetString());
        Assert.Equal("order-747", request.GetProperty("OrderId").GetString());
        Assert.Equal(
            TBankSignature.ComputeToken(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["TerminalKey"] = TERMINAL,
                    ["OrderId"] = "order-747",
                },
                PASSWORD),
            request.GetProperty("Token").GetString());

        TBankPaymentHistory payment = Assert.Single(result.Payments);
        Assert.Equal("payment-1", payment.PaymentId);
        Assert.Equal(99_000, payment.Amount);
        Assert.Equal("CONFIRMED", payment.Status);
        Assert.True(payment.Success);
        Assert.Equal(0, payment.ErrorCode);
        Assert.Equal("OK", payment.Message);
    }

    [Fact]
    public async Task CheckOrderAsync_ProviderFailure_ReturnsProviderError()
    {
        var handler = new CapturingHandler(
            """{"Success":false,"ErrorCode":"7","Message":"Order not found","OrderId":"order-747","Payments":[]}""");

        var result = await CreateClient(handler).CheckOrderAsync("order-747");

        Assert.True(result.IsFailure);
        Assert.Equal("tbank.check_order.provider_error", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task CheckOrderAsync_MalformedHistory_ReturnsInvalidResponse()
    {
        var handler = new CapturingHandler(
            """{"Success":true,"ErrorCode":"0","OrderId":"order-747"}""");

        var result = await CreateClient(handler).CheckOrderAsync("order-747");

        Assert.True(result.IsFailure);
        Assert.Equal("tbank.check_order.invalid_response", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task CheckOrderAsync_HttpFailureWithoutProviderPayload_ReturnsTransportError()
    {
        var handler = new CapturingHandler("{}", HttpStatusCode.BadGateway);

        var result = await CreateClient(handler).CheckOrderAsync("order-747");

        Assert.True(result.IsFailure);
        Assert.Equal("tbank.check_order.transport_error", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task GetCardListAsync_RebillRecovery_SignsCustomerKeyAndDeserializesCards()
    {
        var handler = new CapturingHandler(
            """
            [
              {
                "CardId": "card-1",
                "Pan": "518223******0036",
                "Status": "A",
                "RebillId": "rebill-1",
                "CardType": 0,
                "ExpDate": "1128"
              }
            ]
            """);

        IReadOnlyList<TBankCard> cards = (await CreateClient(handler)
            .GetCardListAsync("customer-747")).Value;

        Assert.EndsWith("GetCardList", handler.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(handler.RequestBody!);
        JsonElement request = document.RootElement;
        Assert.Equal("customer-747", request.GetProperty("CustomerKey").GetString());
        Assert.Equal(
            TBankSignature.ComputeToken(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["TerminalKey"] = TERMINAL,
                    ["CustomerKey"] = "customer-747",
                },
                PASSWORD),
            request.GetProperty("Token").GetString());

        TBankCard card = Assert.Single(cards);
        Assert.Equal("card-1", card.CardId);
        Assert.Equal("A", card.Status);
        Assert.Equal("rebill-1", card.RebillId);
    }

    [Fact]
    public async Task CheckOrderRetryPolicy_RetriesReadButDoesNotRetryInit()
    {
        var initHandler = new CountingFailureHandler();
        await using (ServiceProvider services = CreateConfiguredProvider(initHandler))
        {
            ITBankClient client = services.GetRequiredService<ITBankClient>();
            await client.InitAsync(new TBankInitRequest
            {
                OrderId = "unsafe-init",
                Amount = 99_000,
            });
        }

        Assert.Equal(1, initHandler.CallCount);

        var checkOrderHandler = new CountingFailureHandler();
        await using (ServiceProvider services = CreateConfiguredProvider(checkOrderHandler))
        {
            ITBankClient client = services.GetRequiredService<ITBankClient>();
            await client.CheckOrderAsync("safe-read");
        }

        Assert.Equal(4, checkOrderHandler.CallCount);
    }

    [Fact]
    public async Task CheckOrderRetryPolicy_RetriesReadTimeout()
    {
        var handler = new CountingTimeoutHandler();
        await using ServiceProvider services = CreateConfiguredProvider(handler);

        await services.GetRequiredService<ITBankClient>().CheckOrderAsync("safe-timeout-read");

        Assert.Equal(4, handler.CallCount);
    }

    private static TBankClient CreateClient(HttpMessageHandler handler)
    {
        const string baseUrl = "https://securepay.tinkoff.ru/v2/";
        var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new PaymentMetrics(services.GetRequiredService<IMeterFactory>());
        var options = Options.Create(new TBankOptions
        {
            TerminalKey = TERMINAL,
            Password = PASSWORD,
            BaseUrl = baseUrl,
        });

        return new TBankClient(http, options, metrics, NullLogger<TBankClient>.Instance);
    }

    private static ServiceProvider CreateConfiguredProvider(HttpMessageHandler handler)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TBank:TerminalKey"] = TERMINAL,
                ["TBank:Password"] = PASSWORD,
                ["TBank:BaseUrl"] = "https://securepay.tinkoff.ru/v2/",
                ["TBank:HttpTimeoutSeconds"] = "1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddPaymentsCore(configuration);
        services.AddTBankClient();
        services.AddHttpClient("tbank").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingHandler(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class CountingFailureHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class CountingTimeoutHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The timeout policy must cancel every attempt");
        }
    }
}
