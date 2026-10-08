using Core.Abstractions;
using EducationContentService.Contracts.Projects;
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

public sealed record GetProjectReviewContextQuery(Guid ProjectId) : IQuery;

public sealed class GetProjectReviewContextEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("projects/{projectId:guid}/review-context",
                async Task<EndpointResult<ProjectReviewContextDto?>> (
                    [FromRoute] Guid projectId,
                    [FromServices] GetProjectReviewContextHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetProjectReviewContextQuery(projectId), ct))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

/// <summary>
///     Возвращает ProjectReviewContext (или null если ещё не создан).
///     Author UI грузит для рендера markdown-редактора guidelines.
/// </summary>
public sealed class GetProjectReviewContextHandler
    : IQueryHandlerWithResult<ProjectReviewContextDto?, GetProjectReviewContextQuery>
{
    private readonly IReviewConfigRepository _reviewConfig;
    private readonly IProjectsRepository _projects;
    private readonly UserScopedData _user;

    public GetProjectReviewContextHandler(
        IReviewConfigRepository reviewConfig,
        IProjectsRepository projects,
        UserScopedData user)
    {
        _reviewConfig = reviewConfig;
        _projects = projects;
        _user = user;
    }

    public async Task<Result<ProjectReviewContextDto?, Error>> Handle(
        GetProjectReviewContextQuery query, CancellationToken ct)
    {
        Result<Project, Error> projectResult = await _projects.GetByAsync(p => p.Id == query.ProjectId, ct);
        if (projectResult.IsFailure) return projectResult.Error;

        UnitResult<Error> ownership = _user.CheckOwnership(projectResult.Value.AuthorId);
        if (ownership.IsFailure) return ownership.Error;

        ProjectReviewContext? ctx = await _reviewConfig.GetProjectReviewContextAsync(
            c => c.ProjectId == query.ProjectId, ct);
        if (ctx is null) return Result.Success<ProjectReviewContextDto?, Error>(null);

        return new ProjectReviewContextDto(
            ctx.Id,
            ctx.ProjectId,
            ctx.GuidelinesMarkdown,
            ctx.IsAutoReviewEnabled,
            ctx.RequiresGithubConnection,
            ctx.RequiresReviewApp,
            ctx.UpdatedAt);
    }
}
