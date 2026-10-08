using CSharpFunctionalExtensions;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory replacement for <see cref="ITelegramBotServiceClient"/>. Used by
/// SetOnboardingEnabled handler to ensure TELEGRAM step on enable when the plan
/// already has chat-bindings. Default behaviour: returns false (no bindings).
/// Tests pre-populate <see cref="ActiveBindingsByPlanId"/> for plans that should
/// be considered as having an active TG-binding. <see cref="ShouldFail"/> simulates
/// soft-degrade path (TG service down).
/// </summary>
public sealed class FakeTelegramBotServiceClient : ITelegramBotServiceClient
{
    public HashSet<Guid> ActiveBindingsByPlanId { get; } = [];

    public bool ShouldFail { get; set; }

    /// <summary>
    /// Result returned by <see cref="CheckPlanMembershipAsync"/>. Tests override per
    /// scenario (member / not_member / unknown). Default: not a member.
    /// </summary>
    public PlanMembershipDto MembershipResult { get; set; } = new(IsMember: false, Status: "not_member");

    public void Reset()
    {
        ActiveBindingsByPlanId.Clear();
        ShouldFail = false;
        MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");
    }

    public Task<Result<bool, Error>> HasActiveChatBindingAsync(
        Guid planId,
        CancellationToken cancellationToken)
    {
        if (ShouldFail)
        {
            return Task.FromResult(
                Result.Failure<bool, Error>(Error.Failure("telegram.unavailable", "stubbed failure")));
        }

        return Task.FromResult(
            Result.Success<bool, Error>(ActiveBindingsByPlanId.Contains(planId)));
    }

    public Task<Result<PlanMembershipDto, Error>> CheckPlanMembershipAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        if (ShouldFail)
        {
            return Task.FromResult(
                Result.Failure<PlanMembershipDto, Error>(
                    Error.Failure("telegram.unavailable", "stubbed failure")));
        }

        return Task.FromResult(
            Result.Success<PlanMembershipDto, Error>(MembershipResult));
    }
}
