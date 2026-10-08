namespace AuthService.Contracts;

public record ProfilesDto(
    StudentProfileDto? Student,
    AuthorProfileDto? Author,
    ReviewerProfileDto? Reviewer
);
