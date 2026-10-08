using Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.Domain.Timecodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;

namespace MaterialProcessingService.Core.Features.Timecodes.GetActiveJobs;

public sealed record GetActiveAiJobsQuery(Guid UserId) : IQuery;

public sealed class GetActiveAiJobsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/material-processing/jobs/active/", async Task<EndpointResult<GetActiveAiJobsResponse>> (
                [FromServices] IQueryHandlerWithResult<GetActiveAiJobsResponse, GetActiveAiJobsQuery> handler,
                [FromServices] UserScopedData user,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetActiveAiJobsQuery(user.UserId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE);
    }
}

/// <summary>
///     Возвращает union активных (QUEUED+PROCESSING) timecode-job'ов и
///     content-job'ов текущего юзера. Используется глобальным AI-jobs tracker'ом
///     на фронте — sticky-bar показывает прогресс с любой страницы. Сортировка
///     по CreatedAt DESC (свежие сверху).
/// </summary>
public sealed class GetActiveAiJobsHandler
    : IQueryHandlerWithResult<GetActiveAiJobsResponse, GetActiveAiJobsQuery>
{
    private readonly ITimecodeGenerationJobRepository _timecodeRepository;
    private readonly IContentGenerationJobRepository _contentRepository;

    public GetActiveAiJobsHandler(
        ITimecodeGenerationJobRepository timecodeRepository,
        IContentGenerationJobRepository contentRepository)
    {
        _timecodeRepository = timecodeRepository;
        _contentRepository = contentRepository;
    }

    public async Task<Result<GetActiveAiJobsResponse, Error>> Handle(
        GetActiveAiJobsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TimecodeGenerationJob> timecodeJobs =
            await _timecodeRepository.GetActiveByUserAsync(query.UserId, cancellationToken);
        IReadOnlyList<ContentGenerationJob> contentJobs =
            await _contentRepository.GetActiveByUserAsync(query.UserId, cancellationToken);

        List<ActiveAiJobDto> jobs = new(timecodeJobs.Count + contentJobs.Count);

        foreach (TimecodeGenerationJob job in timecodeJobs)
        {
            // Mode=TRANSCRIPT_ONLY → user-facing kind «TRANSCRIPT» (без LLM-шага).
            // Иначе full timecode pipeline → «TIMECODES».
            string jobKind = job.Mode == TimecodeGenerationJobMode.TRANSCRIPT_ONLY
                ? "TRANSCRIPT"
                : "TIMECODES";
            jobs.Add(new ActiveAiJobDto(
                job.Id,
                jobKind,
                job.VideoAssetId,
                MaterialId: null,
                job.Status.ToStorageValue(),
                job.Stage.ToStorageValue(),
                job.ProgressPercent,
                job.ErrorCode,
                job.ErrorMessage,
                job.CreatedAt));
        }

        foreach (ContentGenerationJob job in contentJobs)
        {
            jobs.Add(new ActiveAiJobDto(
                job.Id,
                "CONTENT",
                job.VideoAssetId,
                job.MaterialId,
                job.Status.ToStorageValue(),
                job.Stage.ToStorageValue(),
                job.ProgressPercent,
                job.ErrorCode,
                job.ErrorMessage,
                job.CreatedAt));
        }

        // Сортировка после union — свежие сверху.
        jobs.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));

        return new GetActiveAiJobsResponse(jobs);
    }
}
