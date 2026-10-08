using System.Linq.Expressions;
using AccessService.Core.Database;
using AccessService.Core.Features.Plans.UseCases;
using AccessService.Domain;
using CSharpFunctionalExtensions;
using NSubstitute;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

public sealed class GetPlanTelegramInfoHandlerTests
{
    [Fact]
    public async Task Handle_TrialPlan_ReturnsSingleCanonicalLifetimePlan()
    {
        Guid authorId = Guid.CreateVersion7();
        Plan trial = CreatePlan(authorId, "trial", trialDurationDays: 30);
        Plan canonical = CreatePlan(authorId, "canonical");
        Plan secondary = CreatePlan(authorId, "secondary");
        canonical.UpdateIsHighlighted(true);
        canonical.Publish();
        secondary.Publish();

        IPlansRepository plans = Substitute.For<IPlansRepository>();
        plans.GetByAsync(
                Arg.Any<Expression<Func<Plan, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<Plan, Error>(trial));
        plans.GetManyByAsync(
                Arg.Any<Expression<Func<Plan, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns([secondary, canonical]);

        var handler = new GetPlanTelegramInfoHandler(plans);
        var result = await handler.Handle(new GetPlanTelegramInfoQuery(trial.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(canonical.Id, result.Value.CanonicalTelegramPlanId);
    }

    private static Plan CreatePlan(Guid authorId, string slug, int? trialDurationDays = null) =>
        Plan.Create(
            authorId,
            PlanTier.FULL_ALL,
            PlanSlug.Of(slug).Value,
            PlanDisplayName.Of(slug).Value,
            [],
            requestedCapabilities: null,
            trialDurationDays: trialDurationDays).Value;
}
