namespace MaterialProcessingService.IntegrationTests.Infrastructure;

[CollectionDefinition(nameof(IntegrationTestsFixture))]
public sealed class IntegrationTestsFixture : ICollectionFixture<IntegrationTestsWebFactory>;
