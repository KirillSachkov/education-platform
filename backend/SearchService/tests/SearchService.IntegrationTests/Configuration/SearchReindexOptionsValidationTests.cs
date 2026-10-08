using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SearchService.Core;
using SearchService.Core.Reindex;

namespace SearchService.IntegrationTests.Configuration;

public sealed class SearchReindexOptionsValidationTests
{
    [Fact]
    public void Invalid_batch_size_should_fail_options_validation()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SearchReindexOptions:ExportBatchSize"] = "0",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCore(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SearchReindexOptions>>().Value);
    }
}
