namespace CommentService.IntegrationTests.Infrastructure;

[CollectionDefinition(nameof(IntegrationTestsFixture))]
public sealed class IntegrationTestsFixture : ICollectionFixture<IntegrationTestsWebFactory>;
