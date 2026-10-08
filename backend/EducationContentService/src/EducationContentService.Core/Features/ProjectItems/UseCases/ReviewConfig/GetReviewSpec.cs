using Core.Abstractions;
using EducationContentService.Contracts.Issues;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Projects;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ProjectItems.UseCases.ReviewConfig;

public sealed record GetReviewSpecQuery(Guid IssueId) : IQuery;

public sealed class GetReviewSpecEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("issues/{issueId:guid}/review-spec",
                async Task<EndpointResult<ReviewSpecDto?>> (
                    [FromRoute] Guid issueId,
                    [FromServices] GetReviewSpecHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetReviewSpecQuery(issueId), ct))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

/// <summary>
///     Возвращает ReviewSpec для issue (или null если ещё не создан).
///     Author UI грузит это вместе с issue detail чтобы отрендерить AI-проверку секцию.
/// </summary>
public sealed class GetReviewSpecHandler : IQueryHandlerWithResult<ReviewSpecDto?, GetReviewSpecQuery>
{
    private readonly IReviewConfigRepository _reviewConfig;
    private readonly IIssuesRepository _issues;
    private readonly IProjectsRepository _projects;
    private readonly UserScopedData _user;

    public GetReviewSpecHandler(
        IReviewConfigRepository reviewConfig,
        IIssuesRepository issues,
        IProjectsRepository projects,
        UserScopedData user)
    {
        _reviewConfig = reviewConfig;
        _issues = issues;
        _projects = projects;
        _user = user;
    }

    public async Task<Result<ReviewSpecDto?, Error>> Handle(GetReviewSpecQuery query, CancellationToken ct)
    {
        Result<Issue, Error> issueResult = await _issues.GetByAsync(i => i.Id == query.IssueId, ct);
        if (issueResult.IsFailure) return issueResult.Error;

        Result<Project, Error> projectResult = await _projects.GetByAsync(p => p.Id == issueResult.Value.ProjectId, ct);
        if (projectResult.IsFailure) return projectResult.Error;

        UnitResult<Error> ownership = _user.CheckOwnership(projectResult.Value.AuthorId);
        if (ownership.IsFailure) return ownership.Error;

        ReviewSpec? spec = await _reviewConfig.GetReviewSpecAsync(s => s.IssueId == query.IssueId, ct);
        if (spec is null) return Result.Success<ReviewSpecDto?, Error>(null);

        return new ReviewSpecDto(
            spec.Id,
            spec.IssueId,
            spec.AuthorPrompt,
            spec.ReviewAspects,
            spec.IsAutoReviewEnabled,
            spec.UpdatedAt);
    }
}
