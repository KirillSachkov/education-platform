using Core.Abstractions;
using Core.Validation;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Core.Features.Timecodes.GetByVideo;

public sealed record GetVideoTimecodesQuery(Guid VideoId) : IQuery;

public sealed class GetVideoTimecodesValidator : AbstractValidator<GetVideoTimecodesQuery>
{
    public GetVideoTimecodesValidator()
    {
        RuleFor(x => x.VideoId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetVideoTimecodesQuery.VideoId)));
    }
}

public sealed class GetVideoTimecodesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Videos.MANAGE отсекает участников; handler дополнительно проверяет владельца
        // по уже необходимому FileService lookup, не добавляя отдельный HTTP-вызов.
        app.MapGet("/material-processing/videos/{videoId:guid}/", async Task<EndpointResult<GetVideoTimecodesResponse>> (
                [FromRoute] Guid videoId,
                [FromServices] IQueryHandlerWithResult<GetVideoTimecodesResponse, GetVideoTimecodesQuery> handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetVideoTimecodesQuery(videoId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE);
    }
}

public sealed class GetVideoTimecodesHandler
    : IQueryHandlerWithResult<GetVideoTimecodesResponse, GetVideoTimecodesQuery>
{
    private static readonly ContentGenerationStatus[] _activeContentStatuses =
    [
        ContentGenerationStatus.Queued,
        ContentGenerationStatus.Processing,
    ];

    private static readonly TimecodeGenerationStatus[] _activeTimecodeStatuses =
    [
        TimecodeGenerationStatus.Queued,
        TimecodeGenerationStatus.Processing,
    ];

    private readonly IContentGenerationJobRepository _contentGenerationJobRepository;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly ITimecodeGenerationJobRepository _jobRepository;
    private readonly IVideoTranscriptRepository _transcriptRepository;
    private readonly IValidator<GetVideoTimecodesQuery> _validator;
    private readonly ILogger<GetVideoTimecodesHandler> _logger;
    private readonly UserScopedData _user;

    public GetVideoTimecodesHandler(
        IContentGenerationJobRepository contentGenerationJobRepository,
        IFileServiceClient fileServiceClient,
        ITimecodeGenerationJobRepository jobRepository,
        IVideoTranscriptRepository transcriptRepository,
        IValidator<GetVideoTimecodesQuery> validator,
        ILogger<GetVideoTimecodesHandler> logger,
        UserScopedData user)
    {
        _contentGenerationJobRepository = contentGenerationJobRepository;
        _fileServiceClient = fileServiceClient;
        _jobRepository = jobRepository;
        _transcriptRepository = transcriptRepository;
        _validator = validator;
        _logger = logger;
        _user = user;
    }

    public async Task<Result<GetVideoTimecodesResponse, Error>> Handle(
        GetVideoTimecodesQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<GetVideoProcessingSourceResponse?, Error> sourceResult =
            await _fileServiceClient.GetVideoProcessingSourceAsync(query.VideoId, cancellationToken);

        if (sourceResult.IsSuccess && sourceResult.Value is not null)
        {
            UnitResult<Error> ownership = _user.CheckOwnership(sourceResult.Value.UploadedByUserId);
            if (ownership.IsFailure)
                return ownership.Error;
        }
        else if (!_user.IsAdmin)
        {
            return sourceResult.IsFailure
                ? sourceResult.Error
                : GeneralErrors.NotFound(query.VideoId);
        }

        // Репозитории сортируют по CreatedAt DESC. Активная job, если она есть,
        // всегда является самой новой для видео: partial unique index допускает
        // только одну QUEUED/PROCESSING, а новый запуск создаётся после терминальных.
        // Поэтому двух latest-запросов достаточно вместо четырёх active+latest.
        TimecodeGenerationJob? latestJob = await _jobRepository.GetByAsync(
            x => x.VideoAssetId == query.VideoId,
            cancellationToken);
        ContentGenerationJob? latestContentJob = await _contentGenerationJobRepository.GetByAsync(
            x => x.VideoAssetId == query.VideoId,
            cancellationToken);

        TimecodeGenerationJob? activeJob = latestJob is not null
            && _activeTimecodeStatuses.Contains(latestJob.Status)
                ? latestJob
                : null;
        ContentGenerationJob? activeContentJob = latestContentJob is not null
            && _activeContentStatuses.Contains(latestContentJob.Status)
                ? latestContentJob
                : null;

        Guid? currentAssetVersion = ResolveCurrentAssetVersion(
            query.VideoId,
            sourceResult,
            latestJob,
            latestContentJob);

        TranscriptPreparationDto? transcriptPreparation = MapTranscriptPreparation(activeJob, activeContentJob);
        bool hasPersistedTranscript = currentAssetVersion.HasValue &&
            await _transcriptRepository.GetByVideoAssetVersionAsync(
                query.VideoId,
                currentAssetVersion.Value,
                asNoTracking: true,
                cancellationToken: cancellationToken) is not null;
        bool hasTranscript =
            hasPersistedTranscript ||
            HasTranscriptReadyStage(activeJob?.Stage) ||
            HasTranscriptReadyStage(activeContentJob?.Stage);

        TimecodeGenerationJob? generationJob = activeJob;

        if (generationJob is null &&
            latestJob is not null &&
            latestJob.Status == TimecodeGenerationStatus.Failed)
        {
            // Не surface'им FAILED как «текущее состояние», если артефакт прошлой
            // успешной попытки всё ещё актуален для текущего AssetVersion:
            //   • TRANSCRIPT_ONLY failed, но persisted transcript есть → пред-
            //     последний прогон сделал работу, юзер видит зелёный таб «Транскрипт».
            //   • TIMECODES failed, но артефакт (главы) для текущей версии уже есть —
            //     либо был более ранний TIMECODES_COMPLETED, либо главы уже лежат в
            //     Kinescope (заведены вручную / денормализованы прошлым прогоном) без
            //     строки Completed под текущую версию. Баннер «Не удалось» поверх
            //     валидных глав только путает (особенно когда failed at SAVE — главы
            //     ушли в Kinescope, а denormalize в ECS отвалился).
            // Real-time сигнал об ошибке юзер видел через toast в момент фейла; persistent
            // баннер нужен только когда после фейла действительно нечего показать.
            bool isStaleFailure = false;
            if (currentAssetVersion.HasValue && latestJob.AssetVersion == currentAssetVersion.Value)
            {
                if (latestJob.Mode == TimecodeGenerationJobMode.TRANSCRIPT_ONLY)
                {
                    isStaleFailure = hasPersistedTranscript;
                }
                else
                {
                    isStaleFailure =
                        await _jobRepository.ExistsAsync(
                            x => x.VideoAssetId == query.VideoId &&
                                 x.AssetVersion == currentAssetVersion.Value &&
                                 x.Mode == TimecodeGenerationJobMode.TIMECODES &&
                                 x.Status == TimecodeGenerationStatus.Completed,
                            cancellationToken)
                        || await HasExistingChaptersAsync(query.VideoId, cancellationToken);
                }
            }

            if (!isStaleFailure)
                generationJob = latestJob;
        }

        return new GetVideoTimecodesResponse(
            query.VideoId,
            currentAssetVersion ?? generationJob?.AssetVersion ?? latestJob?.AssetVersion ?? latestContentJob?.AssetVersion,
            hasTranscript,
            transcriptPreparation,
            generationJob is null
                ? null
                : new TimecodeGenerationDto(
                    generationJob.Id,
                    generationJob.Status.ToStorageValue(),
                    generationJob.Stage.ToStorageValue(),
                    generationJob.ProgressPercent,
                    generationJob.ErrorCode,
                    generationJob.ErrorMessage,
                    generationJob.CreatedAt,
                    generationJob.Mode.ToString()),
            activeContentJob is null
                ? null
                : new ActiveContentGenerationDto(
                    activeContentJob.Id,
                    activeContentJob.MaterialId,
                    activeContentJob.Status.ToStorageValue(),
                    activeContentJob.Stage.ToStorageValue(),
                    activeContentJob.ProgressPercent,
                    activeContentJob.ErrorCode,
                    activeContentJob.ErrorMessage,
                    activeContentJob.CreatedAt));
    }

    private Guid? ResolveCurrentAssetVersion(
        Guid videoId,
        Result<GetVideoProcessingSourceResponse?, Error> sourceResult,
        TimecodeGenerationJob? latestJob,
        ContentGenerationJob? latestContentJob)
    {
        if (sourceResult.IsSuccess && sourceResult.Value is not null)
            return sourceResult.Value.AssetVersion;

        if (sourceResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to resolve current asset version for video {VideoId}. Falling back to local status data. Error: {Error}",
                videoId,
                sourceResult.Error.GetMessage());
        }

        return latestJob?.AssetVersion ?? latestContentJob?.AssetVersion;
    }

    // Главы уже могут существовать в Kinescope без строки Completed-джобы под текущую
    // версию: автор заводит/правит их вручную («Сохранить главы»), либо их
    // денормализовали прошлым прогоном. В этом случае FAILED-джоба не должна поднимать
    // красный баннер — артефакт уже есть. Вызывается только в терминальной ветке
    // FAILED-без-Completed (не hot-path поллинга), поэтому доп. round-trip в FileService
    // не бьёт по латентности опроса. Ограничение: главы в Kinescope не версионируются,
    // так что после ре-аплоада (новая версия) старые главы тоже подавят баннер —
    // приемлемо: ре-аплоад редок, автор не заблокирован (кнопка «Перегенерировать»), а
    // toast об ошибке был показан в момент фейла.
    private async Task<bool> HasExistingChaptersAsync(Guid videoId, CancellationToken cancellationToken)
    {
        Result<GetVideoChaptersResponse?, Error> chaptersResult =
            await _fileServiceClient.GetVideoChaptersAsync(videoId, cancellationToken);

        return chaptersResult.IsSuccess && chaptersResult.Value is { Chapters.Count: > 0 };
    }

    private static TranscriptPreparationDto? MapTranscriptPreparation(
        TimecodeGenerationJob? activeTimecodeJob,
        ContentGenerationJob? activeContentJob)
    {
        if (activeTimecodeJob is not null &&
            IsTranscriptPreparationStage(activeTimecodeJob.Stage))
        {
            return new TranscriptPreparationDto(
                activeTimecodeJob.Id,
                "TIMECODES",
                activeTimecodeJob.Status.ToStorageValue(),
                activeTimecodeJob.Stage.ToStorageValue(),
                activeTimecodeJob.ProgressPercent,
                null,
                activeTimecodeJob.ErrorCode,
                activeTimecodeJob.ErrorMessage,
                activeTimecodeJob.CreatedAt);
        }

        if (activeContentJob is not null &&
            IsTranscriptPreparationStage(activeContentJob.Stage))
        {
            return new TranscriptPreparationDto(
                activeContentJob.Id,
                "CONTENT",
                activeContentJob.Status.ToStorageValue(),
                activeContentJob.Stage.ToStorageValue(),
                activeContentJob.ProgressPercent,
                activeContentJob.MaterialId,
                activeContentJob.ErrorCode,
                activeContentJob.ErrorMessage,
                activeContentJob.CreatedAt);
        }

        return null;
    }

    private static bool IsTranscriptPreparationStage(TimecodeGenerationStage stage) =>
        stage is TimecodeGenerationStage.Queued or
            TimecodeGenerationStage.SourceFetch or
            TimecodeGenerationStage.Probe or
            TimecodeGenerationStage.AudioExtract or
            TimecodeGenerationStage.Transcribe;

    private static bool IsTranscriptPreparationStage(ContentGenerationStage stage) =>
        stage is ContentGenerationStage.Queued or
            ContentGenerationStage.SourceFetch or
            ContentGenerationStage.Probe or
            ContentGenerationStage.AudioExtract or
            ContentGenerationStage.Transcribe;

    private static bool HasTranscriptReadyStage(TimecodeGenerationStage? stage) =>
        stage is TimecodeGenerationStage.Generate or
            TimecodeGenerationStage.Save;

    private static bool HasTranscriptReadyStage(ContentGenerationStage? stage) =>
        stage is ContentGenerationStage.Generate or
            ContentGenerationStage.Save;
}
