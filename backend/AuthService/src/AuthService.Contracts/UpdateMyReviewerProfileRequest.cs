namespace AuthService.Contracts;

public record UpdateMyReviewerProfileRequest(
    int? ReviewCapacity,
    string? Expertise
);
