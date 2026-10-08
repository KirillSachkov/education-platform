using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Core;
using SearchService.Core.Reindex;
using Typesense.Setup;

namespace SearchService.Infrastructure.Typesense;

public static class Registration
{
    public static IServiceCollection AddInfrastructureTypesense(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TypesenseOptions>()
            .Bind(configuration.GetSection(nameof(TypesenseOptions)))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "TypesenseOptions:ApiKey is required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Url), "TypesenseOptions:Url is required")
            .ValidateOnStart();

        TypesenseOptions opts = configuration.GetSection(nameof(TypesenseOptions)).Get<TypesenseOptions>()
                                ?? new TypesenseOptions();

        services.AddTypesenseClient(
            config =>
            {
                config.ApiKey = opts.ApiKey;
                if (!string.IsNullOrWhiteSpace(opts.Url))
                {
                    var url = new Uri(opts.Url);
                    config.Nodes = [
                        new Node(
                            url.Host,
                            url.Port.ToString(CultureInfo.InvariantCulture),
                            url.Scheme)];
                }
            },
            enableHttpCompression: true);

        services.AddHostedService<TypesenseInitializationBackgroundService>();
        services.AddScoped<ISearchProvider, TypesenseProvider>();
        // Hash схемы детерминирован для билда — singleton, считается один раз (#526).
        services.AddSingleton<ISearchSchemaVersionProvider, TypesenseSchemaVersionProvider>();

        return services;
    }
}
