using System.Text;
using Core.Abstractions;
using Core.Database;
using Dapper;
using FileService.Core.Repositories;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace FileService.Core.Features.Videos.Queries;

public sealed record GetVideoSubtitlesQuery(Guid VideoId) : IQuery;

public sealed class GetVideoSubtitlesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/videos/{videoId:guid}/subtitles.srt/", async Task<IResult> (
                [FromRoute] Guid videoId,
                [FromServices] GetVideoSubtitlesHandler handler,
                CancellationToken cancellationToken) =>
            {
                Result<string, Error> result = await handler.Handle(new GetVideoSubtitlesQuery(videoId), cancellationToken);
                if (result.IsFailure)
                {
                    return result.Error.Type switch
                    {
                        ErrorType.NOT_FOUND => Results.NotFound(),
                        ErrorType.AUTHORIZATION => Results.Forbid(),
                        ErrorType.VALIDATION => Results.BadRequest(result.Error),
                        _ => Results.Problem(result.Error.GetMessage()),
                    };
                }

                return Results.File(
                    Encoding.UTF8.GetBytes(result.Value),
                    "application/x-subrip; charset=utf-8",
                    $"transcript-{videoId:N}.srt");
            })
            .RequirePermissions(PlatformPermissions.Videos.MANAGE);
    }
}

public sealed class GetVideoSubtitlesHandler(
    IMediaAssetRepository assetRepository,
    IVideoProviderRefRepository providerRefRepository,
    ITransactionManager transactionManager,
    UserScopedData user) : IQueryHandlerWithResult<string, GetVideoSubtitlesQuery>
{
    public async Task<Result<string, Error>> Handle(GetVideoSubtitlesQuery query, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult = await assetRepository.GetByAsync(
            asset => asset.Id == query.VideoId, cancellationToken);
        if (assetResult.IsFailure)
            return assetResult.Error;

        MediaAsset asset = assetResult.Value;
        if (asset.Kind != AssetKind.VIDEO || asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
            return GeneralErrors.NotFound(query.VideoId);

        if (!user.IsAdmin && (asset.UploadedByUserId is null || asset.UploadedByUserId != user.UserId))
            return Error.Authorization("video.authorship.required", "Операция доступна только владельцу видео");

        if (asset.Status != AssetStatus.READY)
            return Error.Validation("video.not_ready", "Видео ещё не готово");

        Result<VideoProviderRef, Error> providerResult = await providerRefRepository.GetByAsync(
            reference => reference.AssetId == query.VideoId, cancellationToken);
        if (providerResult.IsFailure)
            return providerResult.Error;

        // Match the provider version used by the former transcript read endpoint.
        // A replaced video never receives a transcript from its previous version.
        string? segmentsJson = await transactionManager.GetDbConnection().QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                """
                SELECT segments_json::text FROM files.video_transcripts
                WHERE video_asset_id = @VideoId AND asset_version = @AssetVersion
                """,
                new { query.VideoId, AssetVersion = providerResult.Value.Version },
                cancellationToken: cancellationToken));

        return segmentsJson is null
            ? GeneralErrors.NotFound(query.VideoId)
            : StoredTranscriptSrt.Render(segmentsJson);
    }
}