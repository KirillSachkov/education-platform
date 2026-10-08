using AccessService.Domain;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// Pure domain tests for <see cref="PlanCapabilitiesMapper"/> round-trip, with focus on
/// the TRAINER_PRO add-on capability (#614): it must round-trip through ToStrings/FromStrings
/// (so the <c>cap:TRAINER_PRO</c> Redis tag is emitted) yet stay OUT of the FULL alias.
/// </summary>
public class PlanCapabilitiesMapperTests
{
    [Fact]
    public void TrainerPro_round_trips_through_strings()
    {
        IReadOnlyList<string> names = PlanCapabilitiesMapper.ToStrings(PlanCapabilities.TRAINER_PRO);

        Assert.Contains("TRAINER_PRO", names);
        Assert.Equal(PlanCapabilities.TRAINER_PRO, PlanCapabilitiesMapper.FromStrings(names));
    }

    [Fact]
    public void Full_alias_does_not_include_TrainerPro()
    {
        Assert.False(PlanCapabilities.FULL.HasFlag(PlanCapabilities.TRAINER_PRO));

        IReadOnlyList<string> names = PlanCapabilitiesMapper.ToStrings(PlanCapabilities.FULL);
        Assert.DoesNotContain("TRAINER_PRO", names);
    }

    [Fact]
    public void TrainerPro_combined_with_other_flags_round_trips()
    {
        PlanCapabilities caps = PlanCapabilities.TRAINER_PRO | PlanCapabilities.VIEW_MATERIALS;

        IReadOnlyList<string> names = PlanCapabilitiesMapper.ToStrings(caps);

        Assert.Equal(["VIEW_MATERIALS", "TRAINER_PRO"], names);
        Assert.Equal(caps, PlanCapabilitiesMapper.FromStrings(names));
    }

    [Fact]
    public void All_individual_flags_round_trip()
    {
        PlanCapabilities all =
            PlanCapabilities.VIEW_MATERIALS
            | PlanCapabilities.SUBMIT_ISSUES
            | PlanCapabilities.CODE_REVIEW
            | PlanCapabilities.COMMUNITY_ACCESS
            | PlanCapabilities.LIVE_CALLS
            | PlanCapabilities.JOB_SUPPORT
            | PlanCapabilities.TRAINER_PRO;

        IReadOnlyList<string> names = PlanCapabilitiesMapper.ToStrings(all);

        Assert.Equal(7, names.Count);
        Assert.Equal(all, PlanCapabilitiesMapper.FromStrings(names));
    }
}
