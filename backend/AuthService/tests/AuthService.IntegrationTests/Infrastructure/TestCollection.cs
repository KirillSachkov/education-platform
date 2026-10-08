namespace AuthService.IntegrationTests.Infrastructure;

[CollectionDefinition(nameof(IntegrationTestFixture))]
public class IntegrationTestFixture : ICollectionFixture<IntegrationTestsWebFactory>
{
}
