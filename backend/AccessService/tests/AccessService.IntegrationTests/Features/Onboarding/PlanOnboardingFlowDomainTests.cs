using AccessService.Domain.Onboarding;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Onboarding;

public sealed class PlanOnboardingFlowDomainTests
{
    private readonly DateTimeOffset _now = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _planId = Guid.NewGuid();

    [Fact]
    public void Create_returns_disabled_flow_with_no_steps()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);

        Assert.Equal(_planId, flow.PlanId);
        Assert.False(flow.IsEnabled);
        Assert.Empty(flow.Steps);
    }

    [Fact]
    public void AddMarkdownStep_appends_with_fractional_order()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);

        Result<PlanOnboardingStep, Error> first = flow.AddMarkdownStep("Привет", "Body 1", true, _now);
        Result<PlanOnboardingStep, Error> second = flow.AddMarkdownStep("Гайд", "Body 2", true, _now);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, flow.Steps.Count);
        Assert.True(string.Compare(first.Value.SortOrder.Value, second.Value.SortOrder.Value, StringComparison.Ordinal) < 0,
            "Second step should sort after first");
    }

    [Fact]
    public void AddMarkdownStep_rejects_empty_title()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);

        Result<PlanOnboardingStep, Error> result = flow.AddMarkdownStep("  ", "Body", true, _now);

        Assert.True(result.IsFailure);
        Assert.Equal("onboarding.step.title.required", result.Error.Messages[0].Code);
    }

    [Fact]
    public void AddMarkdownStep_rejects_empty_body()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);

        Result<PlanOnboardingStep, Error> result = flow.AddMarkdownStep("Title", "", true, _now);

        Assert.True(result.IsFailure);
        Assert.Equal("onboarding.step.body.required", result.Error.Messages[0].Code);
    }

    [Fact]
    public void SetStepIsSkippable_toggles_for_auto_step()
    {
        // Auto-step (TELEGRAM/GITHUB/NOTIFICATIONS) дефолтно IsSkippable=true.
        // Автор должен мочь сделать его обязательным через SetStepIsSkippable.
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);
        PlanOnboardingStep step = flow.EnsureAutoStep(PlanOnboardingStepType.TELEGRAM, _now);
        Assert.True(step.IsSkippable);

        UnitResult<Error> result = flow.SetStepIsSkippable(step.Id, isSkippable: false, _now);
        Assert.True(result.IsSuccess);
        Assert.False(step.IsSkippable);

        UnitResult<Error> result2 = flow.SetStepIsSkippable(step.Id, isSkippable: true, _now);
        Assert.True(result2.IsSuccess);
        Assert.True(step.IsSkippable);
    }

    [Fact]
    public void SetStepIsSkippable_step_not_found()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);

        UnitResult<Error> result = flow.SetStepIsSkippable(Guid.NewGuid(), isSkippable: false, _now);
        Assert.True(result.IsFailure);
        Assert.Equal("onboarding.step.not.found", result.Error.Messages[0].Code);
    }

    [Fact]
    public void EnsureAutoStep_telegram_idempotent()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);

        PlanOnboardingStep first = flow.EnsureAutoStep(PlanOnboardingStepType.TELEGRAM, _now);
        PlanOnboardingStep second = flow.EnsureAutoStep(PlanOnboardingStepType.TELEGRAM, _now);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(flow.Steps);
    }

    [Fact]
    public void EnsureAutoStep_rejects_markdown()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);

        Assert.Throws<ArgumentException>(() =>
            flow.EnsureAutoStep(PlanOnboardingStepType.MARKDOWN, _now));
    }

    [Fact]
    public void RemoveAutoStep_rejects_notifications()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);
        flow.EnsureAutoStep(PlanOnboardingStepType.NOTIFICATIONS, _now);

        Assert.Throws<ArgumentException>(() =>
            flow.RemoveAutoStep(PlanOnboardingStepType.NOTIFICATIONS, _now));
    }

    [Fact]
    public void RemoveAutoStep_github_removes_step()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);
        flow.EnsureAutoStep(PlanOnboardingStepType.GITHUB, _now);
        Assert.Single(flow.Steps);

        flow.RemoveAutoStep(PlanOnboardingStepType.GITHUB, _now);

        Assert.Empty(flow.Steps);
    }

    [Fact]
    public void RemoveStep_rejects_auto_step()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);
        PlanOnboardingStep auto = flow.EnsureAutoStep(PlanOnboardingStepType.GITHUB, _now);

        UnitResult<Error> result = flow.RemoveStep(auto.Id, _now);

        Assert.True(result.IsFailure);
        Assert.Equal("onboarding.step.auto.not.removable", result.Error.Messages[0].Code);
    }

    [Fact]
    public void RemoveStep_removes_markdown_step()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);
        PlanOnboardingStep md = flow.AddMarkdownStep("T", "B", true, _now).Value;

        UnitResult<Error> result = flow.RemoveStep(md.Id, _now);

        Assert.True(result.IsSuccess);
        Assert.Empty(flow.Steps);
    }

    [Fact]
    public void Enable_then_Disable_toggles_IsEnabled()
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(_planId, _now);
        Assert.False(flow.IsEnabled);

        flow.Enable(_now);
        Assert.True(flow.IsEnabled);

        flow.Disable(_now.AddSeconds(1));
        Assert.False(flow.IsEnabled);
    }
}
