using AuthService.Contracts.HttpCommunication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace TelegramBotService.Contracts.HttpCommunication;

/// <summary>
///     Регистрация HTTP-клиента AuthService (Telegram-endpoint) в DI-контейнере.
///     Переиспользует секцию <see cref="AuthServiceOptions"/> и <see cref="TokenForwardingHandler"/>.
/// </summary>
public static class AuthTelegramClientExtensions
{
    public static IServiceCollection AddAuthTelegramClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AuthServiceOptions>(configuration.GetSection(AuthServiceOptions.SECTION_NAME));
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<IAuthTelegramClient, AuthTelegramClient>((sp, client) =>
            {
                AuthServiceOptions options = sp.GetRequiredService<IOptions<AuthServiceOptions>>().Value;

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
