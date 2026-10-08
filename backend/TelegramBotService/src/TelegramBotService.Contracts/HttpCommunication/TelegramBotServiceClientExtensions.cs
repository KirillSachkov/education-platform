using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace TelegramBotService.Contracts.HttpCommunication;

/// <summary>
///     Регистрация HTTP-клиента TelegramBotService в DI-контейнере другого сервиса.
///     Использует <see cref="TokenForwardingHandler"/> для S2S-токена и Polly retry/circuit.
/// </summary>
public static class TelegramBotServiceClientExtensions
{
    public static IServiceCollection AddTelegramBotServiceHttpCommunication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TelegramBotServiceOptions>(
            configuration.GetSection(TelegramBotServiceOptions.SECTION_NAME));
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<ITelegramBotServiceClient, TelegramBotServiceClient>((sp, client) =>
            {
                TelegramBotServiceOptions options =
                    sp.GetRequiredService<IOptions<TelegramBotServiceOptions>>().Value;

                client.BaseAddress = new Uri(options.Url);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .AddHttpMessageHandler<TokenForwardingHandler>()
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1))))
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

        return services;
    }
}
