using System.Collections.ObjectModel;
using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace ProgressService.IntegrationTests.Infrastructure;

public sealed class MockAccessServiceClient : IAccessServiceClient
{
    public Collection<GrantCall> Calls { get; } = [];

    /// <summary>
    /// When set, the next N calls return failure (e.g. 404). Otherwise — success.
    /// Useful when testing graceful degradation when an author has no default plan.
    /// </summary>
    public bool ReturnNotFound { get; set; }

    // Derive-model (epic access-derive-model, Phase 1) configurable test data.
    // Seed these so the derived READ surfaces can be proven to return grant-holders
    // even with ZERO course_enrollments rows.
    private readonly Dictionary<Guid, List<CourseGranteeDto>> _granteesByCourse = [];
    private readonly Dictionary<Guid, List<Guid>> _coveredCoursesByUser = [];

    /// <summary>Register a course grantee (roster member) for <paramref name="courseId"/>.</summary>
    public void AddCourseGrantee(
        Guid courseId,
        Guid userId,
        string source = "ADMIN_GRANT",
        string planTier = "FULL_ALL",
        DateTimeOffset? grantedAt = null,
        Guid? grantId = null)
    {
        if (!_granteesByCourse.TryGetValue(courseId, out List<CourseGranteeDto>? list))
        {
            list = [];
            _granteesByCourse[courseId] = list;
        }

        list.Add(new CourseGranteeDto(
            userId,
            source,
            grantedAt ?? DateTimeOffset.UtcNow,
            planTier,
            grantId ?? Guid.NewGuid()));
    }

    /// <summary>Register that <paramref name="userId"/>'s active grants cover <paramref name="courseId"/>.</summary>
    public void AddCoveredCourse(Guid userId, Guid courseId)
    {
        if (!_coveredCoursesByUser.TryGetValue(userId, out List<Guid>? list))
        {
            list = [];
            _coveredCoursesByUser[userId] = list;
        }

        if (!list.Contains(courseId))
        {
            list.Add(courseId);
        }
    }

    public void Reset()
    {
        Calls.Clear();
        ReturnNotFound = false;
        _granteesByCourse.Clear();
        _coveredCoursesByUser.Clear();
    }

    public Task<Result<PlanGrantDto, Error>> GrantLifetimeForAuthorAsync(
        Guid userId,
        Guid authorId,
        string source,
        Guid? sourceRef,
        CancellationToken cancellationToken)
    {
        Calls.Add(new GrantCall(userId, authorId, source, sourceRef));

        if (ReturnNotFound)
        {
            return Task.FromResult(Result.Failure<PlanGrantDto, Error>(
                Error.NotFound("plan.not.found", "Plan not found")));
        }

        PlanGrantDto dto = new(
            Guid.NewGuid(), userId, Guid.NewGuid(),
            source, sourceRef, DateTimeOffset.UtcNow, ExpiresAt: null,
            Status: "ACTIVE", RevokedAt: null, RevokeReason: null);

        return Task.FromResult(Result.Success<PlanGrantDto, Error>(dto));
    }

    public sealed record GrantCall(Guid UserId, Guid AuthorId, string Source, Guid? SourceRef);

    public Task<Result<PlanGrantDto, Error>> GrantByPlanAsync(
        Guid userId,
        Guid planId,
        string source,
        Guid? sourceRef,
        CancellationToken cancellationToken)
    {
        if (ReturnNotFound)
        {
            return Task.FromResult(Result.Failure<PlanGrantDto, Error>(
                Error.NotFound("plan.not.found", "Plan not found")));
        }

        PlanGrantDto dto = new(
            Guid.NewGuid(), userId, planId,
            source, sourceRef, DateTimeOffset.UtcNow, ExpiresAt: null,
            Status: "ACTIVE", RevokedAt: null, RevokeReason: null);

        return Task.FromResult(Result.Success<PlanGrantDto, Error>(dto));
    }

    public Task<Result<IReadOnlyList<PlanGrantDto>, Error>> GetUserGrantsAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<IReadOnlyList<PlanGrantDto>, Error>(new List<PlanGrantDto>()));

    public Task<Result<IReadOnlyList<Guid>, Error>> GetLifetimeGranteeUserIdsAsync(
        Guid authorId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<IReadOnlyList<Guid>, Error>(new List<Guid>()));

    public Task<Result<CourseGranteesPage, Error>> GetCourseGranteesAsync(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (ReturnNotFound)
        {
            return Task.FromResult(Result.Failure<CourseGranteesPage, Error>(
                Error.NotFound("course.not.found", "Course not found")));
        }

        List<CourseGranteeDto> all = _granteesByCourse.TryGetValue(courseId, out List<CourseGranteeDto>? list)
            ? list
            : [];

        if (userIdsFilter is { Count: > 0 })
        {
            HashSet<Guid> filter = [.. userIdsFilter];
            all = all.Where(g => filter.Contains(g.UserId)).ToList();
        }

        // Single-page mock — returns the full (filtered) roster; cursor is ignored. Tests
        // exercising pagination keep rosters within one page.
        return Task.FromResult(Result.Success<CourseGranteesPage, Error>(
            new CourseGranteesPage(all, NextCursor: null, TotalCount: all.Count)));
    }

    public Task<Result<CoveredCoursesResult, Error>> GetUserCoveredCoursesAsync(
        Guid userId,
        Guid? authorId,
        CancellationToken cancellationToken)
    {
        if (ReturnNotFound)
        {
            return Task.FromResult(Result.Failure<CoveredCoursesResult, Error>(
                Error.NotFound("user.not.found", "User not found")));
        }

        IReadOnlyList<Guid> ids = _coveredCoursesByUser.TryGetValue(userId, out List<Guid>? list)
            ? list
            : [];

        return Task.FromResult(Result.Success<CoveredCoursesResult, Error>(
            new CoveredCoursesResult(ids)));
    }

    // ProgressService never calls this — Telegram welcome-message lookup is TelegramBotService-only.
    // Stub returns NotFound so the mock satisfies the interface without affecting any test.
    public Task<Result<PlanTelegramInfoDto, Error>> GetPlanTelegramInfoAsync(
        Guid planId,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Failure<PlanTelegramInfoDto, Error>(
            Error.NotFound("plan.not.found", "Plan not found")));
}
