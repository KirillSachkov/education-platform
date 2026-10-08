namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
///     Page response для author-side <c>GET /access/plans/{id}/grants/</c> с keyset-pagination.
///     <see cref="NextCursor"/> = <c>null</c> когда страниц больше нет.
/// </summary>
public sealed record PlanGrantsPageDto(
    IReadOnlyList<PlanGrantDto> Items,
    string? NextCursor);
