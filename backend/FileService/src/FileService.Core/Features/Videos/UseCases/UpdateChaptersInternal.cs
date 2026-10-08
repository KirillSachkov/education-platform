using Core.Abstractions;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
using FileService.Core.Services;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace FileService.Core.Features.Videos.UseCases;

public sealed record UpdateVideoChaptersCommand(
    Guid VideoId,
    Guid GenerationJobId,
    Guid AssetVersion,
    IReadOnlyList<UpdateVideoChapterItemDto> Chapters) : ICommand;

public sealed class UpdateVideoChaptersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/internal/videos/{videoId:guid}/chapters/", async Task<EndpointResult<GetVideoChaptersResponse>> (
                [FromRoute] Guid videoId,
                [FromBody] UpdateVideoChaptersRequest request,
                [FromServices] ICommandHandler<GetVideoChaptersResponse, UpdateVideoChaptersCommand> handler,
                CancellationToken token) =>
                await handler.Handle(
                    new UpdateVideoChaptersCommand(
                        videoId,
                        request.GenerationJobId,
                        request.AssetVersion,
                        request.Chapters),
                    token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class UpdateVideoChaptersHandler : ICommandHandler<GetVideoChaptersResponse, UpdateVideoChaptersCommand>
{
    private readonly IVideoProviderRefRepository _providerRefRepository;
    private readonly IVideoProvider _videoProvider;

    public UpdateVideoChaptersHandler(
        IVideoProviderRefRepository providerRefRepository,
        IVideoProvider videoProvider)
    {
        _providerRefRepository = providerRefRepository;
        _videoProvider = videoProvider;
    }

    public async Task<Result<GetVideoChaptersResponse, Error>> Handle(
        UpdateVideoChaptersCommand command,
        CancellationToken cancellationToken)
    {
        if (command.VideoId == Guid.Empty)
            return GeneralErrors.ValueIsRequired(nameof(command.VideoId));

        if (command.GenerationJobId == Guid.Empty)
            return GeneralErrors.ValueIsRequired(nameof(command.GenerationJobId));

        if (command.AssetVersion == Guid.Empty)
            return GeneralErrors.ValueIsRequired(nameof(command.AssetVersion));

        Result<VideoProviderRef, Error> providerRefResult =
            await _providerRefRepository.GetByAsync(x => x.AssetId == command.VideoId, cancellationToken);

        if (providerRefResult.IsFailure)
        {
            if (providerRefResult.Error.Type == ErrorType.NOT_FOUND)
            {
                return Error.Validation(
                    "video.chapters.provider_ref_required",
                    "Видео ещё не привязано к провайдеру и не может принять главы");
            }

            return providerRefResult.Error;
        }

        if (providerRefResult.Value.Version != command.AssetVersion)
        {
            return Error.Conflict(
                "video.chapters.asset_version_mismatch",
                "Версия видео изменилась, главы нужно сгенерировать заново");
        }

        Result<IReadOnlyList<Features.Videos.VideoChapterDefinition>, Error> chaptersResult =
            Features.Videos.VideoChapterValidation.ValidateGenerated(command.Chapters);
        if (chaptersResult.IsFailure)
            return chaptersResult.Error;

        UnitResult<Error> replaceResult = await _videoProvider.ReplaceChaptersAsync(
            providerRefResult.Value.ExternalAssetId,
            chaptersResult.Value,
            cancellationToken);
        if (replaceResult.IsFailure)
            return replaceResult.Error;

        return new GetVideoChaptersResponse(
            command.VideoId,
            chaptersResult.Value
                .OrderBy(x => x.StartSeconds)
                .Select((chapter, index) => new VideoChapterDto(
                    $"chapter-{index + 1}",
                    chapter.Title,
                    chapter.StartSeconds,
                    index))
                .ToArray());
    }
}
