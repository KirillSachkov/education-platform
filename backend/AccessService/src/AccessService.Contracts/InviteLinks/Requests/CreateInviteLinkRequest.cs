namespace AccessService.Contracts.InviteLinks.Requests;

public sealed record CreateInviteLinkRequest(
    bool MultiUse,
    int? MaxUses,
    DateTimeOffset? ExpiresAt,
    string? Label
);
