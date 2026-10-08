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
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.Core.Configuration;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Core.Features.Timecodes.Generate;

public sealed record GenerateVideoTimecodesCommand(
    Guid VideoId,
    /// <summary>
    ///     Optional admin model override (e.g. "openai/gpt-4o-mini-transcribe" для STT-prep,
    ///     "openai/gpt-4.1-nano" для timecode-генерации). Игнорируется если caller
    ///     не admin. Persists на job → background handler использует.
    /// </summary>
    string? ModelOverride = null,
    /// <summary>
    ///     Optional admin override для requestedByUserId. Admin client_credentials токен
    ///     (mcp-admin) имеет sub=client_id → UserId=Guid.Empty, и Job.Create reject'ит такой
    ///     job. Bulk-runner передаёт явный admin Guid. Игнорируется если caller не admin.
    /// </summary>
    Guid? RequestedBy = null) : ICommand;

public sealed class GenerateVideoTimecodesValidator : AbstractValidator<GenerateVideoTimecodesCommand>
{
    public GenerateVideoTimecodesValidator()
    {
        RuleFor(x => x.VideoId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GenerateVideoTimecodesCommand.VideoId)));

        RuleFor(x => x.ModelOverride)
            .MaximumLength(AiModelSlot.MAX_MODEL_LENGTH)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GenerateVideoTimecodesCommand.ModelOverride)));
    }
}

public sealed class GenerateVideoTimecodesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/material-processing/videos/{videoId:guid}/timecode-generations/", async Task<EndpointResult<GenerateVideoTimecodesResponse>> (
                [FromRoute] Guid videoId,
                [FromQuery(Name = "modelOverride")] string? modelOverride,
                [FromQuery(Name = "requestedBy")] Guid? requestedBy,
                [FromServices] ICommandHandler<GenerateVideoTimecodesResponse, GenerateVideoTimecodesCommand> handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GenerateVideoTimecodesCommand(videoId, modelOverride, requestedBy), cancellationToken))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE)
            .RequireRateLimiting("ai-generation");
    }
}

public sealed class GenerateVideoTimecodesHandler
    : ICommandHandler<GenerateVideoTimecodesResponse, GenerateVideoTimecodesCommand>
{
    private readonly IFileServiceClient _fileServiceClient;
    private readonly TimecodeJobEnqueuer _enqueuer;
    private readonly UserScopedData _user;
    private readonly IOptionsMonitor<AiPipelineFeatureFlags> _featureFlags;
    private readonly IValidator<GenerateVideoTimecodesCommand> _validator;

    public GenerateVideoTimecodesHandler(
        IFileServiceClient fileServiceClient,
        TimecodeJobEnqueuer enqueuer,
        UserScopedData user,
        IOptionsMonitor<AiPipelineFeatureFlags> featureFlags,
        IValidator<GenerateVideoTimecodesCommand> validator)
    {
        _fileServiceClient = fileServiceClient;
        _enqueuer = enqueuer;
        _user = user;
        _featureFlags = featureFlags;
        _validator = validator;
    }

    public async Task<Result<GenerateVideoTimecodesResponse, Error>> Handle(
        GenerateVideoTimecodesCommand command,
        CancellationToken cancellationToken)
    {
        if (!_featureFlags.CurrentValue.Enabled)
            return Error.Failure("ai.pipeline.disabled", "AI-обработка временно отключена администратором");

        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<GetVideoProcessingSourceResponse?, Error> sourceResult =
            await _fileServiceClient.GetVideoProcessingSourceAsync(command.VideoId, cancellationToken);

        if (sourceResult.IsFailure)
            return sourceResult.Error;

        if (sourceResult.Value is null)
            return GeneralErrors.NotFound(command.VideoId);

        // Ownership: только владелец видео (или admin) может запускать генерацию.
        // Без этого checkа любой platform-author мог бы тратить чужой AI-бюджет (BOLA).
        UnitResult<Error> ownership = _user.CheckOwnership(sourceResult.Value.UploadedByUserId);
        if (ownership.IsFailure)
            return ownership.Error;

        Result<ProcessingSourceType, Error> sourceTypeResult =
            ProcessingSourceType.Create(sourceResult.Value.SourceType);
        if (sourceTypeResult.IsFailure)
            return sourceTypeResult.Error;

        // ModelOverride accept'им только от caller'а с Ai.OVERRIDE_MODEL permission.
        // Обычные авторы могут передать query param, но он игнорируется → используется
        // default из appsettings (или DB-override из /admin/ai-models).
        string? modelOverride = _user.HasPermission(PlatformPermissions.Ai.OVERRIDE_MODEL)
            ? command.ModelOverride
            : null;

        // Service-token (client_credentials) имеет sub=client_id → UserId=Guid.Empty.
        // Bulk-runner передаёт явный requestedBy через query-param. Caller'ы без
        // Ai.IMPERSONATE_REQUESTED_BY permission не могут override'ить — используется
        // их собственный UserId.
        Guid requestedByUserId = _user.UserId != Guid.Empty
            ? _user.UserId
            : (_user.HasPermission(PlatformPermissions.Ai.IMPERSONATE_REQUESTED_BY)
                && command.RequestedBy is { } adminBy
                    ? adminBy
                    : Guid.Empty);

        Result<TimecodeEnqueueResult, Error> enqueueResult = await _enqueuer.EnqueueAsync(
            command.VideoId,
            sourceResult.Value.AssetVersion,
            sourceTypeResult.Value,
            requestedByUserId,
            modelOverride,
            TimecodeTriggerSource.MANUAL,
            materialId: null,
            dedupByVersion: false,
            cancellationToken);
        if (enqueueResult.IsFailure)
            return enqueueResult.Error;

        return new GenerateVideoTimecodesResponse(
            enqueueResult.Value.JobId,
            command.VideoId,
            enqueueResult.Value.Status.ToStorageValue());
    }
}
