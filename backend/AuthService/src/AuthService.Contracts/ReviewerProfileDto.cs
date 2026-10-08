namespace AuthService.Contracts;

public record ReviewerProfileDto(
    int ReviewCapacity,
    string? Expertise
);
