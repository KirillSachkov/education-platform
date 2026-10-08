using System.Data.Common;
using System.Text;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;

namespace ProgressService.Core.Features.RoadmapProgress.Queries;

public sealed record GetRoadmapProgressQuery(
    IReadOnlyCollection<RoadmapProgressItemRequest> Items) : IQuery;

public sealed class GetRoadmapProgressQueryValidator : AbstractValidator<GetRoadmapProgressQuery>
{
    private static readonly HashSet<string> _validEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Issue", "Module", "Project", "Material", "Course"
    };

    public GetRoadmapProgressQueryValidator()
    {
        // Each condition needs its own .WithError() — FluentValidation's default message
        // (e.g., "'Items' must not be empty.") would break the JSON-encoded error pipeline
        // in Core.Validation.ValidationExtensions.ToError(), which expects a JSON Error blob
        // in the failure message.
        RuleFor(x => x.Items)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("items"));

        RuleFor(x => x.Items)
            .Must(items => items == null || items.Count <= 200)
            .WithError(GeneralErrors.ValueIsInvalid("items"));

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.EntityId)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired("entityId"));

            item.RuleFor(x => x.EntityType)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired("entityType"));

            item.RuleFor(x => x.EntityType)
                .Must(t => _validEntityTypes.Contains(t))
                .WithError(GeneralErrors.ValueIsInvalid("entityType"));
        });
    }
}

public sealed class GetRoadmapProgressEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/roadmap-progress",
                async Task<EndpointResult<GetRoadmapProgressResponse>> (
                    [FromBody] GetRoadmapProgressRequest request,
                    [FromServices] GetRoadmapProgressHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetRoadmapProgressQuery(request.Items), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

public sealed class GetRoadmapProgressHandler
    : IQueryHandlerWithResult<GetRoadmapProgressResponse, GetRoadmapProgressQuery>
{
    private readonly IValidator<GetRoadmapProgressQuery> _validator;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public GetRoadmapProgressHandler(
        IValidator<GetRoadmapProgressQuery> validator,
        ITransactionManager transactionManager,
        UserScopedData user)
    {
        _validator = validator;
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<Result<GetRoadmapProgressResponse, Error>> Handle(
        GetRoadmapProgressQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        RoadmapProgressItemRequest[] requestedItems = query.Items.Distinct().ToArray();

        DbConnection connection = _transactionManager.GetDbConnection();

        // Split items by entity type. Each non-empty bucket contributes one SELECT to the
        // bundled query below; empty buckets are skipped so we don't burn ANY-comparisons
        // for nothing.
        Guid[] issueIds = requestedItems
            .Where(i => string.Equals(i.EntityType, "Issue", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.EntityId).ToArray();
        Guid[] moduleIds = requestedItems
            .Where(i => string.Equals(i.EntityType, "Module", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.EntityId).ToArray();
        Guid[] projectIds = requestedItems
            .Where(i => string.Equals(i.EntityType, "Project", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.EntityId).ToArray();
        Guid[] materialIds = requestedItems
            .Where(i => string.Equals(i.EntityType, "Material", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.EntityId).ToArray();
        RoadmapProgressItemRequest[] courses = requestedItems
            .Where(i => string.Equals(i.EntityType, "Course", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Issue #217 — was: 5 sequential `QueryAsync` calls on the same connection
        // (wall time = 5 × RTT + Σ query_time). Now: enrollments + one SELECT per
        // non-empty entity bucket, concatenated and sent through `QueryMultipleAsync`
        // — one round-trip, PG processes the bucket SELECTs in order inside one
        // transaction.
        //
        // Per-entity SELECTs use `DISTINCT ON (id) ... ORDER BY id, completed_at DESC
        // NULLS LAST` so users with multiple enrollments touching the same course
        // (rare, but legal during plan-grant re-issuance) collapse deterministically
        // to the most-recent progress row. Replaces the defensive client-side
        // `GroupBy().First()` from before.
        const string enrollmentsSql = """
                                      SELECT ce.id AS enrollment_id, ce.course_id
                                      FROM course_enrollments ce
                                      WHERE ce.user_id = @UserId;
                                      """;
        const string issuesSql = """
                                 SELECT DISTINCT ON (ip.issue_id)
                                     ip.issue_id, ip.status, ip.completed_at, ce.course_id
                                 FROM issue_progress ip
                                 JOIN course_enrollments ce ON ce.id = ip.enrollment_id
                                 WHERE ce.user_id = @UserId
                                   AND ip.issue_id = ANY(@IssueIds)
                                 ORDER BY ip.issue_id, ip.completed_at DESC NULLS LAST;
                                 """;
        const string modulesSql = """
                                  SELECT DISTINCT ON (mp.module_id)
                                      mp.module_id, mp.status, mp.completed_at, ce.course_id
                                  FROM module_progress mp
                                  JOIN course_enrollments ce ON ce.id = mp.enrollment_id
                                  WHERE ce.user_id = @UserId
                                    AND mp.module_id = ANY(@ModuleIds)
                                  ORDER BY mp.module_id, mp.completed_at DESC NULLS LAST;
                                  """;
        const string projectsSql = """
                                   SELECT DISTINCT ON (pp.project_id)
                                       pp.project_id, pp.status, pp.completed_at, ce.course_id
                                   FROM project_progress pp
                                   JOIN course_enrollments ce ON ce.id = pp.enrollment_id
                                   WHERE ce.user_id = @UserId
                                     AND pp.project_id = ANY(@ProjectIds)
                                   ORDER BY pp.project_id, pp.completed_at DESC NULLS LAST;
                                   """;
        // material_views — user-scoped (one row per (user, material)). No JOIN, no
        // duplicate hazard. CourseId in the response is echoed from the request item.
        // is_completed=TRUE — silent track-view'ы (#285) не считаются «изучено» в roadmap'е.
        const string materialsSql = """
                                    SELECT mv.material_id, COALESCE(mv.completed_at, mv.viewed_at) AS viewed_at
                                    FROM material_views mv
                                    WHERE mv.user_id = @UserId
                                      AND mv.material_id = ANY(@MaterialIds)
                                      AND mv.is_completed = TRUE;
                                    """;

        StringBuilder bundled = new();
        bundled.Append(enrollmentsSql);
        if (issueIds.Length > 0) bundled.Append(issuesSql);
        if (moduleIds.Length > 0) bundled.Append(modulesSql);
        if (projectIds.Length > 0) bundled.Append(projectsSql);
        if (materialIds.Length > 0) bundled.Append(materialsSql);

        var parameters = new
        {
            UserId = _user.UserId,
            IssueIds = issueIds,
            ModuleIds = moduleIds,
            ProjectIds = projectIds,
            MaterialIds = materialIds,
        };

        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(bundled.ToString(), parameters,
                cancellationToken: cancellationToken));

        List<EnrollmentRow> enrollments = (await multi.ReadAsync<EnrollmentRow>()).ToList();
        List<Guid> enrolledCourseIds = enrollments.Select(e => e.CourseId).ToList();

        Dictionary<Guid, IssueProgressRow> progressByIssue = issueIds.Length > 0
            ? (await multi.ReadAsync<IssueProgressRow>()).ToDictionary(r => r.IssueId)
            : [];
        Dictionary<Guid, ModuleProgressRow> progressByModule = moduleIds.Length > 0
            ? (await multi.ReadAsync<ModuleProgressRow>()).ToDictionary(r => r.ModuleId)
            : [];
        Dictionary<Guid, ProjectProgressRow> progressByProject = projectIds.Length > 0
            ? (await multi.ReadAsync<ProjectProgressRow>()).ToDictionary(r => r.ProjectId)
            : [];
        Dictionary<Guid, DateTime> viewsByMaterial = materialIds.Length > 0
            ? (await multi.ReadAsync<MaterialViewRow>()).ToDictionary(r => r.MaterialId, r => r.ViewedAt)
            : [];

        // Preserve the original ordering — one DTO per request item, in request order.
        var result = new List<RoadmapProgressItemDto>(requestedItems.Length);

        foreach (RoadmapProgressItemRequest item in requestedItems)
        {
            if (string.Equals(item.EntityType, "Issue", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(progressByIssue.TryGetValue(item.EntityId, out IssueProgressRow? r)
                    ? new RoadmapProgressItemDto("Issue", item.EntityId, r.CourseId, r.Status, r.CompletedAt)
                    : new RoadmapProgressItemDto("Issue", item.EntityId, item.CourseId, "NOT_STARTED", null));
            }
            else if (string.Equals(item.EntityType, "Module", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(progressByModule.TryGetValue(item.EntityId, out ModuleProgressRow? r)
                    ? new RoadmapProgressItemDto("Module", item.EntityId, r.CourseId, r.Status, r.CompletedAt)
                    : new RoadmapProgressItemDto("Module", item.EntityId, item.CourseId, "NOT_STARTED", null));
            }
            else if (string.Equals(item.EntityType, "Project", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(progressByProject.TryGetValue(item.EntityId, out ProjectProgressRow? r)
                    ? new RoadmapProgressItemDto("Project", item.EntityId, r.CourseId, r.Status, r.CompletedAt)
                    : new RoadmapProgressItemDto("Project", item.EntityId, item.CourseId, "NOT_STARTED", null));
            }
            else if (string.Equals(item.EntityType, "Material", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(viewsByMaterial.TryGetValue(item.EntityId, out DateTime viewedAt)
                    ? new RoadmapProgressItemDto("Material", item.EntityId, item.CourseId, "VIEWED", viewedAt)
                    : new RoadmapProgressItemDto("Material", item.EntityId, item.CourseId, "NOT_VIEWED", null));
            }
        }

        if (courses.Length > 0)
        {
            HashSet<Guid> enrolledSet = enrolledCourseIds.ToHashSet();
            foreach (RoadmapProgressItemRequest item in courses)
            {
                string status = enrolledSet.Contains(item.EntityId) ? "ENROLLED" : "NOT_ENROLLED";
                result.Add(new RoadmapProgressItemDto(
                    "Course", item.EntityId, item.CourseId, status, null));
            }
        }

        return new GetRoadmapProgressResponse(result, enrolledCourseIds);
    }

    private sealed class EnrollmentRow
    {
        public Guid EnrollmentId { get; init; }
        public Guid CourseId { get; init; }
    }

    private sealed class IssueProgressRow
    {
        public Guid IssueId { get; init; }
        public string Status { get; init; } = null!;
        public DateTime? CompletedAt { get; init; }
        public Guid CourseId { get; init; }
    }

    private sealed class ModuleProgressRow
    {
        public Guid ModuleId { get; init; }
        public string Status { get; init; } = null!;
        public DateTime? CompletedAt { get; init; }
        public Guid CourseId { get; init; }
    }

    private sealed class ProjectProgressRow
    {
        public Guid ProjectId { get; init; }
        public string Status { get; init; } = null!;
        public DateTime? CompletedAt { get; init; }
        public Guid CourseId { get; init; }
    }

    private sealed class MaterialViewRow
    {
        public Guid MaterialId { get; init; }
        public DateTime ViewedAt { get; init; }
    }
}
