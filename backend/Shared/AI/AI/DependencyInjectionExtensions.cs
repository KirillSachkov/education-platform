using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Shared.AI;

public static class DependencyInjectionExtensions
{
    /// <summary>
    ///     Multi-provider registration entrypoint.
    ///     <para>
    ///     Config shape (canonical):
    ///     <code>
    ///     "AI": {
    ///       "Default": "aitunnel",
    ///       "Providers": {
    ///         "aitunnel": { "Kind": "OpenAiCompatible", "ApiKey": "...", "BaseUrl": "https://api.aitunnel.ru/v1/", "TimeoutSeconds": 900 },
    ///         "polza":    { "Kind": "OpenAiCompatible", "ApiKey": "...", "BaseUrl": "https://api.polza.ai/api/v1/", "TimeoutSeconds": 900, "SendProviderRoutingHints": true }
    ///       }
    ///     }
    ///     </code>
    ///     </para>
    ///     <para>
    ///     Backward-compat (legacy single-provider config):
    ///     <code>
    ///     "AI": { "Kind": "OpenAiCompatible", "ApiKey": "...", "BaseUrl": "...", "TimeoutSeconds": 900 }
    ///     </code>
    ///     Транслируется в один провайдер с именем <c>default</c>.
    ///     </para>
    /// </summary>
    public static IServiceCollection AddAi(
        this IServiceCollection services,
        IConfigurationSection aiSection,
        Action<AiProviderBuilder> configureProviders)
    {
        AiProvidersOptions providersOptions = aiSection.Get<AiProvidersOptions>() ?? new AiProvidersOptions();
        ValidateOptionsResult validationResult = AiProvidersOptionsValidator.Validate(providersOptions);
        if (validationResult.Failed)
            throw new InvalidOperationException(validationResult.FailureMessage);

        services
            .AddOptions<AiProvidersOptions>()
            .Bind(aiSection)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AiProvidersOptions>, AiProvidersOptionsValidator>();

        AiProviderBuilder providerBuilder = new();
        configureProviders(providerBuilder);

        // Verify every effective provider has a registered adapter (early fail before runtime).
        IReadOnlyDictionary<string, AiOptions> effective = providersOptions.GetEffectiveProviders();
        foreach ((string name, AiOptions providerOptions) in effective)
        {
            if (!providerBuilder.Adapters.ContainsKey(providerOptions.Kind))
            {
                throw new InvalidOperationException(
                    $"AI provider '{name}' uses Kind='{providerOptions.Kind}' which is not registered. " +
                    $"Registered adapters: {string.Join(", ", providerBuilder.Adapters.Keys)}");
            }
        }

        // Shared-services hook: each adapter registers HttpClient + resilience pipeline once.
        providerBuilder.ApplySharedRegistrations(services);

        services.AddSingleton(providerBuilder);
        services.AddSingleton<AiClientFactory>();
        services.AddSingleton<IAiClientFactory>(sp => sp.GetRequiredService<AiClientFactory>());
        services.AddSingleton<IAiTranscriptionClientFactory>(sp => sp.GetRequiredService<AiClientFactory>());
        services.AddSingleton<IAiEmbeddingsClientFactory>(sp => sp.GetRequiredService<AiClientFactory>());

        return services;
    }
}
