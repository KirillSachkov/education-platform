using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Installations.Services;
using AssignmentReviewService.Core.Features.Webhooks.UseCases;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AuthService.Contracts.HttpCommunication;
using Core.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using SharedKernel;
using SharedKernel.Exceptions;

namespace AssignmentReviewService.UnitTests.Webhooks;

public sealed class HandleGitHubWebhookReliabilityTests
{
    private const string Secret = "webhook-test-secret";

    [Fact]
    public async Task Persistence_failure_should_escape_and_not_mark_delivery_processed()
    {
        IVcsInstallationsRepository installations = Substitute.For<IVcsInstallationsRepository>();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        IDatabase database = Substitute.For<IDatabase>();
        VcsInstallation installation = VcsInstallation.Create(
            VcsProvider.GITHUB,
            "42",
            VcsInstallationOwnerType.USER,
            "student",
            "123",
            Guid.CreateVersion7(),
            RepoSelections.AllRepos());
        installations.GetByAsync(
                Arg.Any<Expression<Func<VcsInstallation, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(installation);
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(GeneralErrors.DatabaseError());
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        database.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                When.NotExists)
            .Returns(true);
        HandleGitHubWebhookHandler sut = CreateSut(installations, transactions, redis);
        byte[] body = Encoding.UTF8.GetBytes("{\"action\":\"suspend\",\"installation\":{\"id\":42}}");

        await Assert.ThrowsAsync<TransientException>(() => sut.HandleAsync(
            body,
            ComputeSignature(body),
            "installation",
            "delivery-42",
            CancellationToken.None));

        await database.DidNotReceive().StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            When.NotExists);
    }

    private static HandleGitHubWebhookHandler CreateSut(
        IVcsInstallationsRepository installations,
        ITransactionManager transactions,
        IConnectionMultiplexer redis) => new(
        installations,
        Substitute.For<IAiReviewsRepository>(),
        Substitute.For<IStudentPrMessagesRepository>(),
        Substitute.For<IVcsProvider>(),
        Substitute.For<IOutboxService>(),
        Substitute.For<IAuthServiceClient>(),
        transactions,
        TimeProvider.System,
        Options.Create(new GitHubAppOptions { WebhookSecret = Secret }),
        Substitute.For<ILogger<HandleGitHubWebhookHandler>>(),
        redis);

    private static string ComputeSignature(byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }
}
