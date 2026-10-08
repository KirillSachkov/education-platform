using System.Linq.Expressions;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Features.Lifecycle.IntegrationEvents;
using ProgressService.Domain.Projects;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.Lifecycle;

public sealed class IssuePublishedHandlerTests
{
    [Fact]
    public async Task Save_failure_is_rethrown_for_message_retry()
    {
        Guid projectId = Guid.CreateVersion7();
        ProjectProgress progress = ProjectProgress.Create(Guid.CreateVersion7(), projectId, 1).Value;

        IProjectProgressRepository repository = Substitute.For<IProjectProgressRepository>();
        repository.GetManyByAsync(
                Arg.Any<Expression<Func<ProjectProgress, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns([progress]);

        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();
        transactionManager.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        var handler = new IssuePublishedHandler(
            repository,
            transactionManager,
            NullLogger<IssuePublishedHandler>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => handler.Handle(
            new IssuePublished(Guid.CreateVersion7(), projectId),
            CancellationToken.None));
    }
}
