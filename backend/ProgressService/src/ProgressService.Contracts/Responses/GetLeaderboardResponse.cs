using ProgressService.Contracts.Dtos;

namespace ProgressService.Contracts.Responses;

public sealed record GetLeaderboardResponse(
    IReadOnlyList<LeaderboardUserDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    LeaderboardUserDto? CurrentUser);
