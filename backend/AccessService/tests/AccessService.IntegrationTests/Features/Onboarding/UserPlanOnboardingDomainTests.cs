using AccessService.Domain.Onboarding;
using CSharpFunctionalExtensions;

namespace AccessService.IntegrationTests.Features.Onboarding;

public sealed class UserPlanOnboardingDomainTests
{
    private readonly DateTimeOffset _now = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();

    [Fact]
    public void Start_creates_pending_onboarding()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);

        Assert.Equal(_userId, o.UserId);
        Assert.Equal(_planId, o.PlanId);
        Assert.Equal(_now, o.StartedAt);
        Assert.Null(o.CompletedAt);
        Assert.False(o.IsCompleted);
        Assert.Empty(o.SkippedStepIds);
        Assert.Empty(o.CompletedStepIds);
    }

    [Fact]
    public void SkipStep_adds_to_skipped_list()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        Guid stepId = Guid.NewGuid();

        UnitResult<SharedKernel.Error> result = o.SkipStep(stepId);

        Assert.True(result.IsSuccess);
        Assert.Contains(stepId, o.SkippedStepIds);
    }

    [Fact]
    public void SkipStep_idempotent_on_already_skipped()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        Guid stepId = Guid.NewGuid();
        o.SkipStep(stepId);

        UnitResult<SharedKernel.Error> result = o.SkipStep(stepId);

        Assert.True(result.IsSuccess);
        Assert.Single(o.SkippedStepIds);
    }

    [Fact]
    public void CompleteStep_moves_from_skipped_to_completed()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        Guid stepId = Guid.NewGuid();
        o.SkipStep(stepId);

        UnitResult<SharedKernel.Error> result = o.CompleteStep(stepId);

        Assert.True(result.IsSuccess);
        Assert.Empty(o.SkippedStepIds);
        Assert.Contains(stepId, o.CompletedStepIds);
    }

    [Fact]
    public void Complete_fails_if_pending_steps_exist()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        Guid completed = Guid.NewGuid();
        Guid pending = Guid.NewGuid();
        o.CompleteStep(completed);

        UnitResult<SharedKernel.Error> result = o.Complete(new[] { completed, pending }, _now);

        Assert.True(result.IsFailure);
        Assert.Equal("onboarding.has.pending.steps", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Complete_succeeds_when_all_resolved()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        Guid skipped = Guid.NewGuid();
        Guid completed = Guid.NewGuid();
        o.SkipStep(skipped);
        o.CompleteStep(completed);

        UnitResult<SharedKernel.Error> result = o.Complete(new[] { skipped, completed }, _now);

        Assert.True(result.IsSuccess);
        Assert.True(o.IsCompleted);
        Assert.Equal(_now, o.CompletedAt);
        Assert.Null(o.CurrentStepId);
    }

    [Fact]
    public void SkipStep_after_completion_returns_error()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        o.Complete(Array.Empty<Guid>(), _now);

        UnitResult<SharedKernel.Error> result = o.SkipStep(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("onboarding.already.completed", result.Error.Messages[0].Code);
    }

    [Fact]
    public void AdvanceTo_picks_first_pending_step_in_order()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        Guid s1 = Guid.NewGuid();
        Guid s2 = Guid.NewGuid();
        Guid s3 = Guid.NewGuid();
        o.CompleteStep(s1);

        o.AdvanceTo(new[] { s1, s2, s3 });

        Assert.Equal(s2, o.CurrentStepId);
    }

    [Fact]
    public void AdvanceTo_returns_null_when_all_resolved()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        Guid s1 = Guid.NewGuid();
        Guid s2 = Guid.NewGuid();
        o.CompleteStep(s1);
        o.SkipStep(s2);

        o.AdvanceTo(new[] { s1, s2 });

        Assert.Null(o.CurrentStepId);
    }

    [Fact]
    public void Complete_idempotent_on_already_completed()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        o.Complete(Array.Empty<Guid>(), _now);
        DateTimeOffset originalCompletedAt = o.CompletedAt!.Value;

        UnitResult<SharedKernel.Error> result = o.Complete(Array.Empty<Guid>(), _now.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(originalCompletedAt, o.CompletedAt);
    }

    [Fact]
    public void ReturnToStep_moves_cursor_without_clearing_history()
    {
        Guid s1 = Guid.NewGuid();
        Guid s2 = Guid.NewGuid();
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        o.SetCurrentStep(s1);
        o.CompleteStep(s1);
        o.SetCurrentStep(s2);

        UnitResult<SharedKernel.Error> result = o.ReturnToStep(s1);

        Assert.True(result.IsSuccess);
        Assert.Equal(s1, o.CurrentStepId);
        Assert.Contains(s1, o.CompletedStepIds); // история не очищается
    }

    [Fact]
    public void ReturnToStep_rejects_when_completed()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        o.Complete(Array.Empty<Guid>(), _now);

        UnitResult<SharedKernel.Error> result = o.ReturnToStep(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("onboarding.already.completed", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Reset_clears_all_state_and_sets_first_step()
    {
        Guid s1 = Guid.NewGuid();
        Guid s2 = Guid.NewGuid();
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);
        o.SetCurrentStep(s1);
        o.CompleteStep(s1);
        o.SkipStep(s2);
        o.Complete(new[] { s1, s2 }, _now);

        DateTimeOffset later = _now.AddDays(1);
        o.Reset(new[] { s1, s2 }, later);

        Assert.Null(o.CompletedAt);
        Assert.False(o.IsCompleted);
        Assert.Empty(o.CompletedStepIds);
        Assert.Empty(o.SkippedStepIds);
        Assert.Equal(s1, o.CurrentStepId);
        Assert.Equal(later, o.StartedAt);
    }

    [Fact]
    public void Reset_with_empty_flow_sets_current_to_null()
    {
        UserPlanOnboarding o = UserPlanOnboarding.Start(_userId, _planId, _now);

        o.Reset(Array.Empty<Guid>(), _now);

        Assert.Null(o.CurrentStepId);
        Assert.Null(o.CompletedAt);
    }
}
