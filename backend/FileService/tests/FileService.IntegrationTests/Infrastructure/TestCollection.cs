namespace FileService.IntegrationTests.Infrastructure;

[CollectionDefinition(nameof(IntegrationTestsFixture))]
public class IntegrationTestsFixture : ICollectionFixture<IntegrationTestsWebFactory>;
