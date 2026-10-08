using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using MaterialProcessingService.Contracts.AiSettings.Dtos;
using MaterialProcessingService.Contracts.AiSettings.Requests;
using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.AiSettings;

namespace MaterialProcessingService.Core.Features.AiSettings;

public sealed record UpdateAiModelSettingsCommand(
    AiModelSlotDto SpeechToText,
    AiModelSlotDto TimecodeGeneration,
    AiModelSlotDto ContentGeneration,
    bool AutoProcessVideosEnabled) : ICommand;

public sealed class UpdateAiModelSettingsValidator : AbstractValidator<UpdateAiModelSettingsCommand>
{
    public UpdateAiModelSettingsValidator()
    {
        RuleFor(x => x.SpeechToText).NotNull();
        RuleFor(x => x.TimecodeGeneration).NotNull();
        RuleFor(x => x.ContentGeneration).NotNull();
    }
}

public sealed class UpdateAiModelSettingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/material-processing/admin/ai-settings/",
                async Task<EndpointResult<AiModelSettingsDto>> (
                    [FromBody] UpdateAiModelSettingsRequest request,
                    [FromServices] ICommandHandler<AiModelSettingsDto, UpdateAiModelSettingsCommand> handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new UpdateAiModelSettingsCommand(
                            request.SpeechToText,
                            request.TimecodeGeneration,
                            request.ContentGeneration,
                            request.AutoProcessVideosEnabled),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }
}

public sealed class UpdateAiModelSettingsHandler
    : ICommandHandler<AiModelSettingsDto, UpdateAiModelSettingsCommand>
{
    private readonly IAiModelSettingsRepository _repository;
    private readonly IAiModelSettingsResolver _resolver;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateAiModelSettingsCommand> _validator;

    public UpdateAiModelSettingsHandler(
        IAiModelSettingsRepository repository,
        IAiModelSettingsResolver resolver,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<UpdateAiModelSettingsCommand> validator)
    {
        _repository = repository;
        _resolver = resolver;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<AiModelSettingsDto, Error>> Handle(
        UpdateAiModelSettingsCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        Result<AiModelSlot, Error> sttSlotResult = ToSlot(command.SpeechToText);
        if (sttSlotResult.IsFailure)
            return sttSlotResult.Error;

        Result<AiModelSlot, Error> timecodeSlotResult = ToSlot(command.TimecodeGeneration);
        if (timecodeSlotResult.IsFailure)
            return timecodeSlotResult.Error;

        Result<AiModelSlot, Error> contentSlotResult = ToSlot(command.ContentGeneration);
        if (contentSlotResult.IsFailure)
            return contentSlotResult.Error;

        Guid userId = _user.UserId;
        AiModelSettings? existing = await _repository.GetAsync(asNoTracking: false, cancellationToken);

        if (existing is null)
        {
            Result<AiModelSettings, Error> createResult = AiModelSettings.Create(
                sttSlotResult.Value,
                timecodeSlotResult.Value,
                contentSlotResult.Value,
                command.AutoProcessVideosEnabled,
                userId);

            if (createResult.IsFailure)
                return createResult.Error;

            await _repository.AddAsync(createResult.Value, cancellationToken);
        }
        else
        {
            UnitResult<Error> updateResult = existing.UpdateAll(
                sttSlotResult.Value,
                timecodeSlotResult.Value,
                contentSlotResult.Value,
                command.AutoProcessVideosEnabled,
                userId);

            if (updateResult.IsFailure)
                return updateResult.Error;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _resolver.Invalidate();
        EffectiveAiModelSettings effective = await _resolver.GetAsync(cancellationToken);
        return GetAiModelSettingsEndpoint.MapToDto(effective);
    }

    private static Result<AiModelSlot, Error> ToSlot(AiModelSlotDto dto) =>
        AiModelSlot.Create(dto.Model, dto.Temperature, dto.MaxOutputTokens, dto.TimeoutSeconds);
}
