using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Installations.Services;
using AssignmentReviewService.Core.Features.Installations.UseCases;
using AssignmentReviewService.Core.Vcs;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlatformAuth.Middleware;
using Shared.GitHubApp;
using SharedKernel;

namespace AssignmentReviewService.UnitTests.Installations;

public sealed class CompleteInstallationReliabilityTests
{
    [Fact]
    public async Task Session_mismatch_should_not_burn_single_use_state()
    {
        const string token = "victim-state-token";
        InstallStateData state = new(Guid.CreateVersion7(), "/settings/integrations");
        IInstallStateStore<InstallStateData> store = Substitute.For<IInstallStateStore<InstallStateData>>();
        store.ConsumeAsync(token).Returns(state);

        var caller = new UserScopedData();
        caller.Authenticate(Guid.CreateVersion7(), "attacker", "attacker@example.com", []);

        var sut = new CompleteInstallationHandler(
            store,
            Substitute.For<IVcsProvider>(),
            Substitute.For<IVcsInstallationsRepository>(),
            Substitute.For<IOutboxService>(),
            Substitute.For<ITransactionManager>(),
            caller,
            TimeProvider.System,
            NullLogger<CompleteInstallationHandler>.Instance);

        Result<string, Error> result = await sut.Handle(12345, token, CancellationToken.None);

        Assert.True(result.IsFailure);
        await store.Received(1).SetAsync(token, state, Arg.Any<TimeSpan>());
    }
}
