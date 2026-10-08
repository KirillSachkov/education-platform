using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.AI;
using Shared.AI.OpenAiCompatible;

namespace AI.OpenAiCompatible.Tests;

/// <summary>
///     Multi-provider routing: registry создаёт per-provider клиентов с правильным
///     <see cref="AiOptions"/>, кэширует их, и резолвит по имени.
/// </summary>
public sealed class AiClientFactoryTests
{
    [Fact]
    public void Get_ReturnsDistinctClientsForDifferentProviders()
    {
        using ServiceProvider sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Default"] = "aitunnel",
            ["AI:Providers:aitunnel:Kind"] = "OpenAiCompatible",
            ["AI:Providers:aitunnel:ApiKey"] = "aitunnel-key",
            ["AI:Providers:aitunnel:BaseUrl"] = "https://api.aitunnel.ru/v1/",
            ["AI:Providers:aitunnel:TimeoutSeconds"] = "60",
            ["AI:Providers:polza:Kind"] = "OpenAiCompatible",
            ["AI:Providers:polza:ApiKey"] = "polza-key",
            ["AI:Providers:polza:BaseUrl"] = "https://api.polza.ai/api/v1/",
            ["AI:Providers:polza:TimeoutSeconds"] = "60",
        });

        IAiClientFactory factory = sp.GetRequiredService<IAiClientFactory>();
        IAiClient aitunnel = factory.Get("aitunnel");
        IAiClient polza = factory.Get("polza");

        Assert.NotSame(aitunnel, polza);
        Assert.Equal("aitunnel", factory.DefaultProviderName);
    }

    [Fact]
    public void Get_CachesClientPerProviderName()
    {
        using ServiceProvider sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Default"] = "aitunnel",
            ["AI:Providers:aitunnel:Kind"] = "OpenAiCompatible",
            ["AI:Providers:aitunnel:ApiKey"] = "k",
            ["AI:Providers:aitunnel:BaseUrl"] = "https://api.aitunnel.ru/v1/",
            ["AI:Providers:aitunnel:TimeoutSeconds"] = "60",
        });

        IAiClientFactory factory = sp.GetRequiredService<IAiClientFactory>();
        IAiClient first = factory.Get("aitunnel");
        IAiClient second = factory.Get("aitunnel");

        Assert.Same(first, second);
    }

    [Fact]
    public void Get_FallsBackToDefault_WhenProviderNameIsEmpty()
    {
        using ServiceProvider sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Default"] = "aitunnel",
            ["AI:Providers:aitunnel:Kind"] = "OpenAiCompatible",
            ["AI:Providers:aitunnel:ApiKey"] = "k",
            ["AI:Providers:aitunnel:BaseUrl"] = "https://api.aitunnel.ru/v1/",
            ["AI:Providers:aitunnel:TimeoutSeconds"] = "60",
        });

        IAiClientFactory factory = sp.GetRequiredService<IAiClientFactory>();
        IAiClient resolved = factory.Get(null);
        IAiClient explicitDefault = factory.Get("aitunnel");

        Assert.Same(resolved, explicitDefault);
    }

    [Fact]
    public void Get_Throws_WhenProviderUnknown()
    {
        using ServiceProvider sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Default"] = "aitunnel",
            ["AI:Providers:aitunnel:Kind"] = "OpenAiCompatible",
            ["AI:Providers:aitunnel:ApiKey"] = "k",
            ["AI:Providers:aitunnel:BaseUrl"] = "https://api.aitunnel.ru/v1/",
            ["AI:Providers:aitunnel:TimeoutSeconds"] = "60",
        });

        IAiClientFactory factory = sp.GetRequiredService<IAiClientFactory>();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => factory.Get("unknown"));

        Assert.Contains("'unknown'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacySingleProvider_TranslatedTo_DefaultEntry()
    {
        // Legacy config: верхние поля AI:{Kind,BaseUrl,...} без AI:Providers:* —
        // транслируется в одного провайдера с именем "default".
        using ServiceProvider sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Kind"] = "OpenAiCompatible",
            ["AI:ApiKey"] = "k",
            ["AI:BaseUrl"] = "https://legacy.test/v1/",
            ["AI:TimeoutSeconds"] = "60",
        });

        IAiClientFactory factory = sp.GetRequiredService<IAiClientFactory>();
        Assert.Equal("default", factory.DefaultProviderName);

        // Resolving "default" should not throw.
        IAiClient client = factory.Get(null);
        Assert.NotNull(client);
    }

    [Fact]
    public void AddAi_Throws_WhenProviderKindNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Default"] = "exotic",
                ["AI:Providers:exotic:Kind"] = "AnthropicNative",
                ["AI:Providers:exotic:ApiKey"] = "k",
                ["AI:Providers:exotic:BaseUrl"] = "https://x/v1/",
                ["AI:Providers:exotic:TimeoutSeconds"] = "60",
            })
            .Build();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => services.AddAi(
                config.GetSection(AiOptions.SECTION_NAME),
                static providers => providers.AddOpenAiCompatible()));

        Assert.Contains("AnthropicNative", ex.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildServiceProvider(Dictionary<string, string?> config)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        services.AddAi(
            configuration.GetSection(AiOptions.SECTION_NAME),
            static providers => providers.AddOpenAiCompatible());

        return services.BuildServiceProvider();
    }
}
