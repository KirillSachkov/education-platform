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
using PlatformAuth.Middleware;

namespace FileService.Core.Features.Videos.UseCases;

public sealed record ReplaceVideoChaptersCommand(
    Guid VideoId,
    IReadOnlyList<ReplaceVideoChapterItemDto> Chapters) : ICommand;

public sealed class ReplaceVideoChaptersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/videos/{videoId:guid}/chapters/", async Task<EndpointResult<GetVideoChaptersResponse>> (
                [FromRoute] Guid videoId,
                [FromBody] ReplaceVideoChaptersRequest request,
                [FromServices] ICommandHandler<GetVideoChaptersResponse, ReplaceVideoChaptersCommand> handler,
                CancellationToken token) =>
                await handler.Handle(new ReplaceVideoChaptersCommand(videoId, request.Chapters), token))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE);
    }
}

public sealed class ReplaceVideoChaptersHandler : ICommandHandler<GetVideoChaptersResponse, ReplaceVideoChaptersCommand>
{
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IVideoProviderRefRepository _providerRefRepository;
    private readonly IVideoProvider _videoProvider;
    private readonly UserScopedData _user;

    public ReplaceVideoChaptersHandler(
        IMediaAssetRepository assetRepository,
        IVideoProviderRefRepository providerRefRepository,
        IVideoProvider videoProvider,
        UserScopedData user)
    {
        _assetRepository = assetRepository;
        _providerRefRepository = providerRefRepository;
        _videoProvider = videoProvider;
        _user = user;
    }

    public async Task<Result<GetVideoChaptersResponse, Error>> Handle(
        ReplaceVideoChaptersCommand command,
        CancellationToken cancellationToken)
    {
        if (command.VideoId == Guid.Empty)
            return GeneralErrors.ValueIsRequired(nameof(command.VideoId));

        Result<MediaAsset, Error> assetResult =
            await _assetRepository.GetByAsync(x => x.Id == command.VideoId, cancellationToken);
        if (assetResult.IsFailure)
            return assetResult.Error;

        MediaAsset asset = assetResult.Value;
        if (asset.Kind != AssetKind.VIDEO)
            return Error.Validation("asset.kind.invalid", "Главы можно изменить только у видео");

        bool isPrivileged = _user.IsAdmin;
        if (!isPrivileged && asset.UploadedByUserId != _user.UserId)
            return Error.Authorization("video.not.owner", "Нет доступа к данному видео");

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

        if (!isPrivileged &&
            await _providerRefRepository.HasExternalAssetOwnedByAnotherUserAsync(
                providerRefResult.Value.ExternalAssetId,
                _user.UserId,
                cancellationToken))
        {
            return Error.Authorization(
                "video.provider.not.owner",
                "Внешнее видео связано с ресурсом другого автора");
        }

        Result<IReadOnlyList<Features.Videos.VideoChapterDefinition>, Error> chaptersResult =
            Features.Videos.VideoChapterValidation.ValidateManual(command.Chapters);
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
