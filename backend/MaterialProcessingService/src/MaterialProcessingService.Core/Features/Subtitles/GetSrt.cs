using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using IResult = Microsoft.AspNetCore.Http.IResult;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Core.Subtitles;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Domain.Transcripts;

namespace MaterialProcessingService.Core.Features.Subtitles;

/// <summary>
///     Отдаёт транскрипцию видео в формате <c>.srt</c> для скачивания.
///     Возвращает 404 если транскрипция ещё не подготовлена. Доступно только
///     владельцу видео (или admin).
/// </summary>
public sealed class GetVideoSubtitlesSrtEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/material-processing/videos/{videoId:guid}/subtitles.srt",
                async Task<IResult> (
                    [FromRoute] Guid videoId,
                    [FromServices] IFileServiceClient fileServiceClient,
                    [FromServices] IVideoTranscriptRepository transcriptRepository,
                    [FromServices] ISubtitleRenderer subtitleRenderer,
                    [FromServices] UserScopedData user,
                    CancellationToken cancellationToken) =>
                {
                    if (videoId == Guid.Empty)
                        return Results.BadRequest(GeneralErrors.ValueIsRequired(nameof(videoId)));

                    Result<GetVideoProcessingSourceResponse?, Error> sourceResult =
                        await fileServiceClient.GetVideoProcessingSourceAsync(videoId, cancellationToken);
                    if (sourceResult.IsFailure)
                        return Results.Problem(sourceResult.Error.GetMessage(), statusCode: 502);
                    if (sourceResult.Value is null)
                        return Results.NotFound();

                    UnitResult<Error> ownership = user.CheckOwnership(sourceResult.Value.UploadedByUserId);
                    if (ownership.IsFailure)
                        return Results.Forbid();

                    VideoTranscript? transcript = await transcriptRepository.GetByVideoAssetVersionAsync(
                        videoId,
                        sourceResult.Value.AssetVersion,
                        asNoTracking: true,
                        cancellationToken: cancellationToken);

                    if (transcript is null)
                        return Results.NotFound();

                    Transcript renderable = ToRenderableTranscript(transcript);
                    string srt = subtitleRenderer.RenderSrt(renderable);

                    return Results.File(
                        fileContents: System.Text.Encoding.UTF8.GetBytes(srt),
                        contentType: "application/x-subrip; charset=utf-8",
                        fileDownloadName: $"transcript-{videoId:N}.srt");
                })
            .RequirePermissions(PlatformPermissions.Videos.MANAGE);
    }

    private static Transcript ToRenderableTranscript(VideoTranscript transcript)
    {
        TranscriptSegment[] segments = transcript.Segments.Items
            .Select(s => new TranscriptSegment(
                TimeSpan.FromSeconds(s.Range.StartSeconds),
                TimeSpan.FromSeconds(s.Range.EndSeconds),
                s.Text.Value))
            .ToArray();

        return new Transcript(transcript.Language.Value, segments);
    }
}
