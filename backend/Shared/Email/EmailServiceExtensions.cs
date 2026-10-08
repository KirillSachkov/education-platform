using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Email;

public static class EmailServiceExtensions
{
    /// <summary>
    /// Регистрирует <see cref="IEmailSender"/> на основе секции <c>Email</c> в конфиге.
    /// Если <see cref="EmailOptions.ApiKey"/> задан — используется <see cref="UnisenderEmailSender"/>
    /// (HTTP API). Иначе — <see cref="SmtpEmailSender"/> (MailKit).
    /// </summary>
    public static IServiceCollection AddSharedEmailSender(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SECTION_NAME));

        EmailOptions? opts = configuration.GetSection(EmailOptions.SECTION_NAME).Get<EmailOptions>();
        if (opts?.UseHttpApi == true)
        {
            services.AddHttpClient<IEmailSender, UnisenderEmailSender>(client =>
            {
                client.DefaultRequestHeaders.Add("X-API-KEY", opts.ApiKey);
                client.Timeout = TimeSpan.FromSeconds(10);
            });
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
