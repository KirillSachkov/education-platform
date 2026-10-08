using AssignmentReviewService.Contracts.AiSettings;
using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.AiSettings;
using Core.Abstractions;
using Core.Database;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AssignmentReviewService.Core.Features.Admin.UseCases;

public sealed record UpdateAiSettingsCommand(UpdateAiModelSettingsRequest Request) : ICommand;

public sealed class UpdateAiSettingsValidator : AbstractValidator<UpdateAiSettingsCommand>
{
    public UpdateAiSettingsValidator()
    {
        RuleFor(x => x.Request).NotNull();
        RuleFor(x => x.Request.Reviewer).NotNull();
    }
}

public sealed class UpdateAiSettingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/assignment-review/admin/ai-settings/",
                async Task<EndpointResult> (
                    [FromBody] UpdateAiModelSettingsRequest request,
                    [FromServices] UpdateAiSettingsHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new UpdateAiSettingsCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }
}

/// <summary>
///     Phase 11 (#15) — admin PUT endpoint. Upsert singleton (id = a2c0)
///     + cache invalidation. Reviewer slot должен быть валиден (4xx если не
///     проходит domain validation).
/// </summary>
public sealed class UpdateAiSettingsHandler : ICommandHandler<UpdateAiSettingsCommand>
{
    private readonly IAiModelSettingsRepository _repository;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateAiSettingsCommand> _validator;
    private readonly IAssignmentReviewAiModelSettingsResolver _resolver;

    public UpdateAiSettingsHandler(
        IAiModelSettingsRepository repository,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<UpdateAiSettingsCommand> validator,
        IAssignmentReviewAiModelSettingsResolver resolver)
    {
        _repository = repository;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _resolver = resolver;
    }

    public async Task<UnitResult<Error>> Handle(UpdateAiSettingsCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Error.Validation("ai_settings.invalid", validation.Errors[0].ErrorMessage);

        Result<AiModelSlot, Error> reviewer = AiModelSlot.Create(
            command.Request.Reviewer.Model,
            command.Request.Reviewer.Temperature,
            command.Request.Reviewer.MaxOutputTokens,
            command.Request.Reviewer.TimeoutSeconds);
        if (reviewer.IsFailure) return reviewer.Error;

        AiModelSettings? existing = await _repository.GetSingletonAsync(ct);
        if (existing is null)
        {
            Result<AiModelSettings, Error> created = AiModelSettings.Create(
                reviewer.Value, _user.UserId, command.Request.ReviewerBasePrompt, command.Request.ReviewEnabled,
                command.Request.RepoContextEnabled);
            if (created.IsFailure) return created.Error;
            await _repository.AddAsync(created.Value, ct);
        }
        else
        {
            UnitResult<Error> updated = existing.UpdateAll(
                reviewer.Value, _user.UserId, command.Request.ReviewerBasePrompt, command.Request.ReviewEnabled,
                command.Request.RepoContextEnabled);
            if (updated.IsFailure) return updated.Error;
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        _resolver.InvalidateCache();
        return UnitResult.Success<Error>();
    }
}
