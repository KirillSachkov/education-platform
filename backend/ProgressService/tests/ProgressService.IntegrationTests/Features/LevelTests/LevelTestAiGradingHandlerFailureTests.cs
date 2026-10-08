using System.Linq.Expressions;
using System.Diagnostics.Metrics;
using Core.Database;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Diagnostics;
using ProgressService.Core.Features.LevelTests.AiGrading;
using ProgressService.Core.Features.LevelTests.IntegrationEvents;
using ProgressService.Domain.LevelTests;
using Shared.AI.Skills;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.LevelTests;

public sealed class LevelTestAiGradingHandlerFailureTests
{
    [Fact]
    public async Task Save_failure_before_ai_call_is_rethrown_for_message_retry()
    {
        Guid questionId = Guid.CreateVersion7();
        LevelTestAnswer answer = LevelTestAnswer.Create(questionId, [], "Ответ").Value;
        LevelTestAttempt attempt = LevelTestAttempt.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            anonymousId: null,
            [answer],
            [new LevelTestQuestionKey(questionId, "OPEN_TEXT", "backend", "MIDDLE", [])],
            [new LevelTestSectionDefinition("backend", "Backend", 1, null)],
            [new LevelTestLevelThreshold("BEGINNER", 0)],
            fallbackCourseId: null).Value;

        ILevelTestAttemptRepository repository = Substitute.For<ILevelTestAttemptRepository>();
        repository.GetByAsync(
                Arg.Any<Expression<Func<LevelTestAttempt, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(attempt);

        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();
        transactionManager.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        IOptionsSnapshot<LevelTestAiOptions> options = Substitute.For<IOptionsSnapshot<LevelTestAiOptions>>();
        options.Value.Returns(new LevelTestAiOptions { Enabled = true });

        await using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new ProgressMetrics(services.GetRequiredService<IMeterFactory>());

        var handler = new GradeLevelTestAttemptRequestedHandler(
            repository,
            Substitute.For<IEducationContentServiceClient>(),
            Substitute.For<IStructuredExtractor<LevelTestAiGradesResponse>>(),
            options,
            transactionManager,
            metrics,
            NullLogger<GradeLevelTestAttemptRequestedHandler>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(
            new GradeLevelTestAttemptRequested(attempt.Id),
            CancellationToken.None));
    }
}
