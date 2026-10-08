using AuthService.Contracts.AuthorSpaces;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace NotificationService.Core.Notifications;

internal sealed record CourseRouteContext(
    Guid CourseId,
    string Title,
    string? CourseSlug,
    string? AuthorSlug,
    Guid? AuthorId);

internal static class NotificationRouteContextResolver
{
    public static async Task<CourseRouteContext> ResolveCourseAsync(
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        Guid courseId,
        ILogger logger,
        CancellationToken ct)
    {
        Result<CourseSearchLookupDto, Error> lookup =
            await ecsClient.GetCourseSearchLookupAsync(courseId, ct);

        if (lookup.IsFailure || lookup.Value is null)
        {
            logger.LogWarning(
                "ECS lookup failed for course {CourseId}: {Error}. Using fallback route context.",
                courseId,
                lookup.ErrorText());

            return new CourseRouteContext(
                courseId,
                "курс",
                CourseSlug: null,
                AuthorSlug: null,
                AuthorId: null);
        }

        string? authorSlug = await ResolveAuthorSlugAsync(
            authClient,
            lookup.Value.AuthorId,
            logger,
            ct);

        return new CourseRouteContext(
            courseId,
            lookup.Value.Title,
            lookup.Value.Slug,
            authorSlug,
            lookup.Value.AuthorId);
    }

    public static async Task<string?> ResolveAuthorSlugAsync(
        IAuthServiceClient authClient,
        Guid? authorId,
        ILogger logger,
        CancellationToken ct)
    {
        if (authorId is null)
            return null;

        Result<AuthorSpaceRouteResponse, Error> lookup =
            await authClient.GetAuthorSpaceByAuthorIdAsync(authorId.Value, ct);

        if (lookup.IsSuccess && lookup.Value is not null)
            return lookup.Value.Slug;

        logger.LogWarning(
            "Auth author-space lookup failed for author {AuthorId}: {Error}.",
            authorId,
            lookup.ErrorText());
        return null;
    }
}
