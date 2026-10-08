using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SearchService.Domain;
using Typesense;

namespace SearchService.Infrastructure.Typesense;

/// <summary>
/// Creates the initial collection alias during host startup. This deliberately blocks
/// readiness so startup reindex cannot race and overwrite an alias swap with an empty
/// initial collection.
/// </summary>
public sealed class TypesenseInitializationBackgroundService : IHostedService
{
    private readonly ITypesenseClient _typesenseClient;
    private readonly ILogger<TypesenseInitializationBackgroundService> _logger;

    public TypesenseInitializationBackgroundService(
        ITypesenseClient typesenseClient,
        ILogger<TypesenseInitializationBackgroundService> logger)
    {
        _typesenseClient = typesenseClient;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("TypesenseInitialization started");

            await InitializeCollectionAsync(cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "TypesenseInitialization cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TypesenseInitialization terminated");
            throw;
        }
    }

    public Task StopAsync(CancellationToken _) => Task.CompletedTask;

    private async Task InitializeCollectionAsync(CancellationToken cancellationToken = default)
    {
        bool aliasMissing = false;
        try
        {
            await _typesenseClient.RetrieveCollectionAlias(CollectionNames.EDUCATION_SEARCH, cancellationToken);
        }
        catch (TypesenseApiNotFoundException)
        {
            aliasMissing = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Typesense");
            throw;
        }

        if (aliasMissing)
        {
            await CreateInitialCollectionAsync(cancellationToken);
        }
    }

    private async Task CreateInitialCollectionAsync(CancellationToken cancellationToken)
    {
        string collectionName =
            $"{CollectionNames.EDUCATION_SEARCH}_initial_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.CreateVersion7():N}";

        bool aliasSwapped = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _typesenseClient.CreateCollection(TypesenseSchemas.CreateEducationSearchSchema(collectionName));
            cancellationToken.ThrowIfCancellationRequested();
            await _typesenseClient.UpsertCollectionAlias(
                CollectionNames.EDUCATION_SEARCH,
                new CollectionAlias(collectionName));
            aliasSwapped = true;
        }
        catch
        {
            if (!aliasSwapped && !await AliasTargetsCollectionAsync(collectionName))
            {
                await SafeDeleteCollectionAsync(collectionName);
            }

            throw;
        }

        _logger.LogInformation(
            "Typesense collection alias '{AliasName}' successfully initialized with collection '{CollectionName}'.",
            CollectionNames.EDUCATION_SEARCH,
            collectionName);

        _logger.LogInformation("Typesense initialization completed");
    }

    private async Task<bool> AliasTargetsCollectionAsync(string collectionName)
    {
        try
        {
            CollectionAliasResponse alias = await _typesenseClient.RetrieveCollectionAlias(
                CollectionNames.EDUCATION_SEARCH,
                CancellationToken.None);

            return string.Equals(
                alias.CollectionName,
                collectionName,
                StringComparison.Ordinal);
        }
        catch (TypesenseApiNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // An unknown alias state is safer than deleting a collection that may already
            // be active after a committed server response was lost in transit.
            _logger.LogWarning(
                ex,
                "Could not verify alias ownership for initial Typesense collection {CollectionName}; cleanup skipped",
                collectionName);
            return true;
        }
    }

    private async Task SafeDeleteCollectionAsync(string collectionName)
    {
        try
        {
            await _typesenseClient.DeleteCollection(collectionName, compactStore: true);
        }
        catch (TypesenseApiNotFoundException)
        {
            // The failed create never became durable or another cleanup already removed it.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clean up initial Typesense collection {CollectionName}",
                collectionName);
        }
    }
}
