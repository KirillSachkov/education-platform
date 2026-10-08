using Core.Abstractions;
using Core.Validation;
using Core.Database;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using MaterialProcessingService.Contracts.ContentDrafts.Dtos;
using MaterialProcessingService.Core.Configuration;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.ContentDrafts;

namespace MaterialProcessingService.Core.Features.ContentDrafts.Generate;

public sealed record GenerateVideoContentCommand(
    Guid VideoId,
    GenerateVideoContentRequest Request) : ICommand;

public sealed class GenerateVideoContentRequestValidator : AbstractValidator<GenerateVideoContentRequest>
{
    public GenerateVideoContentRequestValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GenerateVideoContentRequest.MaterialId)));

        RuleFor(x => x.ModelOverride)
            .MaximumLength(AiModelSlot.MAX_MODEL_LENGTH)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GenerateVideoContentRequest.ModelOverride)));
    }
}

public sealed class GenerateVideoContentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/material-processing/videos/{videoId:guid}/content-generations/", async Task<EndpointResult<GenerateVideoContentResponse>> (
                [FromRoute] Guid videoId,
                [FromBody] GenerateVideoContentRequest request,
                [FromServices] ICommandHandler<GenerateVideoContentResponse, GenerateVideoContentCommand> handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GenerateVideoContentCommand(videoId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE)
            .RequireRateLimiting("ai-generation");
    }
}

public sealed class GenerateVideoContentHandler
    : ICommandHandler<GenerateVideoContentResponse, GenerateVideoContentCommand>
{
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IOptionsMonitor<AiPipelineFeatureFlags> _featureFlags;
    private readonly IValidator<GenerateVideoContentRequest> _validator;

    public GenerateVideoContentHandler(
        IContentGenerationJobRepository jobRepository,
        IFileServiceClient fileServiceClient,
        IEducationContentServiceClient educationContentServiceClient,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        UserScopedData user,
        IOptionsMonitor<AiPipelineFeatureFlags> featureFlags,
        IValidator<GenerateVideoContentRequest> validator)
    {
        _jobRepository = jobRepository;
        _fileServiceClient = fileServiceClient;
        _educationContentServiceClient = educationContentServiceClient;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _user = user;
        _featureFlags = featureFlags;
        _validator = validator;
    }

    public async Task<Result<GenerateVideoContentResponse, Error>> Handle(
        GenerateVideoContentCommand command,
        CancellationToken cancellationToken)
    {
        if (!_featureFlags.CurrentValue.Enabled)
            return Error.Failure("ai.pipeline.disabled", "AI-обработка временно отключена администратором");

        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        ContentGenerationJob? activeJob = await _jobRepository.GetActiveByVideoAndMaterialAsync(
            command.VideoId,
            command.Request.MaterialId,
            cancellationToken);

        if (activeJob is not null)
        {
            return new GenerateVideoContentResponse(
                activeJob.Id,
                command.VideoId,
                command.Request.MaterialId,
                activeJob.Status.ToStorageValue());
        }

        Result<GetVideoProcessingSourceResponse?, Error> sourceResult =
            await _fileServiceClient.GetVideoProcessingSourceAsync(command.VideoId, cancellationToken);

        if (sourceResult.IsFailure)
            return sourceResult.Error;

        if (sourceResult.Value is null)
            return GeneralErrors.NotFound(command.VideoId);

        // Ownership: только владелец видео (или admin) может запускать генерацию.
        UnitResult<Error> ownership = _user.CheckOwnership(sourceResult.Value.UploadedByUserId);
        if (ownership.IsFailure)
            return ownership.Error;

        // Защита от молчаливой перезаписи руками написанного автором конспекта:
        // если у материала уже есть Content (markdown тело) и автор не явно
        // подтвердил forceOverwrite — отказываем enqueue до AI работы.
        // Frontend ловит эту ошибку и показывает confirmation: «AI перезапишет
        // ваш текст?» — после согласия повторно шлёт с forceOverwrite=true.
        if (!command.Request.ForceOverwrite)
        {
            Result<MaterialSearchLookupDto, Error> materialLookup =
                await _educationContentServiceClient.GetMaterialSearchLookupAsync(
                    command.Request.MaterialId,
                    cancellationToken);
            if (materialLookup.IsFailure)
                return materialLookup.Error;

            if (!string.IsNullOrWhiteSpace(materialLookup.Value.Content))
            {
                return Error.Conflict(
                    "material.content.exists",
                    "У материала уже есть конспект. Подтвердите перезапись или очистите тело материала.");
            }
        }

        Result<ProcessingSourceType, Error> sourceTypeResult =
            ProcessingSourceType.Create(sourceResult.Value.SourceType);
        if (sourceTypeResult.IsFailure)
            return sourceTypeResult.Error;

        // ModelOverride accept'им только от caller'а с Ai.OVERRIDE_MODEL permission —
        // обычные авторы передают, но получают default из appsettings/DB-override.
        string? modelOverride = _user.HasPermission(PlatformPermissions.Ai.OVERRIDE_MODEL)
            ? command.Request.ModelOverride
            : null;

        // Service-token (client_credentials) имеет sub=client_id → UserId=Guid.Empty.
        // Bulk-runner передаёт явный requestedBy. Caller'ы без Ai.IMPERSONATE_REQUESTED_BY
        // permission используют свой UserId.
        Guid requestedByUserId = _user.UserId != Guid.Empty
            ? _user.UserId
            : (_user.HasPermission(PlatformPermissions.Ai.IMPERSONATE_REQUESTED_BY)
                && command.Request.RequestedBy is { } adminBy
                    ? adminBy
                    : Guid.Empty);

        Result<ContentGenerationJob, Error> jobResult = ContentGenerationJob.Create(
            command.VideoId,
            command.Request.MaterialId,
            sourceResult.Value.AssetVersion,
            requestedByUserId,
            sourceTypeResult.Value,
            modelOverride: modelOverride);
        if (jobResult.IsFailure)
            return jobResult.Error;

        ContentGenerationJob job = jobResult.Value;

        await _jobRepository.AddAsync(job, cancellationToken);
        await _outboxService.PublishAsync(new Processing.GenerateVideoContentJob(job.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            ContentGenerationJob? concurrentJob = await _jobRepository.GetActiveByVideoAndMaterialAsync(
                command.VideoId,
                command.Request.MaterialId,
                cancellationToken);

            if (concurrentJob is not null)
            {
                return new GenerateVideoContentResponse(
                    concurrentJob.Id,
                    command.VideoId,
                    command.Request.MaterialId,
                    concurrentJob.Status.ToStorageValue());
            }

            return saveResult.Error;
        }

        return new GenerateVideoContentResponse(
            job.Id,
            command.VideoId,
            command.Request.MaterialId,
            job.Status.ToStorageValue());
    }
}
