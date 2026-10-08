namespace AccessService.Contracts.Plans.Dtos;

public sealed record PlanStatsDto(
    PlanGrantTotalsDto Totals,
    PlanGrantPeriodCountersDto PeriodCounters,
    IReadOnlyList<PlanGrantSourceBreakdownDto> SourceBreakdown,
    IReadOnlyList<PlanStatsTimeseriesPointDto> Timeseries,
    IReadOnlyList<PlanInviteLinkStatsDto> InviteLinks);

public sealed record PlanGrantTotalsDto(
    long Active,
    long Revoked,
    long Expired,
    long Total);

public sealed record PlanGrantPeriodCountersDto(
    long Last7Days,
    long Last30Days,
    long Last90Days);

public sealed record PlanGrantSourceBreakdownDto(
    string Source,
    long Count);

/// <summary>
/// Daily grants count for the chart. <see cref="BySource"/> is a dictionary
/// of source-name → count to render a stacked line / bar chart.
/// </summary>
public sealed record PlanStatsTimeseriesPointDto(
    DateOnly Day,
    long Count,
    IReadOnlyDictionary<string, long> BySource);

public sealed record PlanInviteLinkStatsDto(
    Guid InviteLinkId,
    string? Label,
    string Token,
    bool IsActive,
    long ActivationsCount,
    long UniqueGrantsCount,
    DateTimeOffset? FirstActivationAt,
    DateTimeOffset? LastActivationAt);
