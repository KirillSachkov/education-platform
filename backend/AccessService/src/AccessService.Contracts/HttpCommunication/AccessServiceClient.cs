using System.Globalization;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Contracts.Plans.Dtos;
using Core.HttpCommunication;
using Microsoft.Extensions.Logging;

namespace AccessService.Contracts.HttpCommunication;

internal sealed class AccessServiceClient : BaseHttpClient, IAccessServiceClient
{
    private const string SERVICE_NAME = "AccessService";

    public AccessServiceClient(HttpClient httpClient, ILogger<AccessServiceClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public Task<Result<PlanTelegramInfoDto, Error>> GetPlanTelegramInfoAsync(
        Guid planId,
        CancellationToken cancellationToken)
        => GetAsync<PlanTelegramInfoDto>(
            $"/internal/access/plans/{planId}/telegram-info",
            cancellationToken);

    public Task<Result<PlanGrantDto, Error>> GrantLifetimeForAuthorAsync(
        Guid userId,
        Guid authorId,
        string source,
        Guid? sourceRef,
        CancellationToken cancellationToken)
        => PostAsync<GrantByAuthorRequest, PlanGrantDto>(
            "/internal/access/grants/lifetime-by-author",
            new GrantByAuthorRequest(userId, authorId, source, sourceRef),
            cancellationToken);

    public Task<Result<PlanGrantDto, Error>> GrantByPlanAsync(
        Guid userId,
        Guid planId,
        string source,
        Guid? sourceRef,
        CancellationToken cancellationToken)
        => PostAsync<GrantByPlanRequest, PlanGrantDto>(
            "/internal/access/grants/by-plan",
            new GrantByPlanRequest(userId, planId, source, sourceRef),
            cancellationToken);

    public Task<Result<IReadOnlyList<PlanGrantDto>, Error>> GetUserGrantsAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<PlanGrantDto>>(
            $"/internal/access/users/{userId}/grants",
            cancellationToken);

    public Task<Result<IReadOnlyList<Guid>, Error>> GetLifetimeGranteeUserIdsAsync(
        Guid authorId,
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<Guid>>(
            $"/internal/access/users/with-lifetime/{authorId}",
            cancellationToken);

    public Task<Result<CourseGranteesPage, Error>> GetCourseGranteesAsync(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
        => GetAsync<CourseGranteesPage>(
            BuildGranteesUrl(courseId, authorId, userIdsFilter, cursor, limit),
            cancellationToken);

    public Task<Result<CoveredCoursesResult, Error>> GetUserCoveredCoursesAsync(
        Guid userId,
        Guid? authorId,
        CancellationToken cancellationToken)
        => PostAsync<CoveredCoursesRequest, CoveredCoursesResult>(
            $"/internal/access/users/{userId}/covered-courses",
            new CoveredCoursesRequest(authorId),
            cancellationToken);

    private static string BuildGranteesUrl(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter,
        string? cursor,
        int? limit)
    {
        List<string> qs = [$"authorId={authorId}"];

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            qs.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (limit is { } l)
        {
            qs.Add($"limit={l.ToString(CultureInfo.InvariantCulture)}");
        }

        if (userIdsFilter is { Count: > 0 })
        {
            qs.AddRange(userIdsFilter.Select(id => $"userIds={id}"));
        }

        return $"/internal/access/courses/{courseId}/grantees?{string.Join('&', qs)}";
    }
}
