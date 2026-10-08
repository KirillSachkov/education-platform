namespace AccessService.Contracts.InviteLinks.Dtos;

public sealed record InviteLinkDto(
    Guid Id,
    Guid PlanId,
    string Token,
    Guid CreatedBy,
    bool MultiUse,
    int? MaxUses,
    int UsageCount,
    DateTimeOffset? ExpiresAt,
    bool IsActive,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);
