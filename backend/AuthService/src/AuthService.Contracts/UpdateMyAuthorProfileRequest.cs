namespace AuthService.Contracts;

public record UpdateMyAuthorProfileRequest(
    string? Specialization,
    string? AboutAsAuthor
);
