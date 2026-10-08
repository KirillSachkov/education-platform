using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Notes;

namespace ProgressService.Core.Features.MaterialNotes.UseCases;

public sealed record UpsertMaterialNoteCommand(Guid MaterialId, string Content) : ICommand;

public sealed class UpsertMaterialNoteCommandValidator : AbstractValidator<UpsertMaterialNoteCommand>
{
    public UpsertMaterialNoteCommandValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpsertMaterialNoteCommand.MaterialId)));
        RuleFor(x => x.Content)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpsertMaterialNoteCommand.Content)));
    }
}

public sealed class UpsertMaterialNoteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/progress/materials/{materialId:guid}/note",
                async Task<EndpointResult<MaterialNoteResponse>> (
                    Guid materialId,
                    UpsertMaterialNoteRequest request,
                    UpsertMaterialNoteHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new UpsertMaterialNoteCommand(materialId, request.Content),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Upsert личной заметки текущего пользователя к материалу: создаёт запись при
///     первом сохранении, дальше обновляет content (autosave с фронта). Own-data —
///     Tier-3 entitlement-чек не нужен (контент материала не возвращается).
///     Лимит — <see cref="MaterialNote.MAX_CONTENT_LENGTH"/> символов (валидируется
///     в domain factory). Issue #465.
/// </summary>
public sealed class UpsertMaterialNoteHandler : ICommandHandler<MaterialNoteResponse, UpsertMaterialNoteCommand>
{
    private readonly IValidator<UpsertMaterialNoteCommand> _validator;
    private readonly IMaterialNoteRepository _noteRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public UpsertMaterialNoteHandler(
        IValidator<UpsertMaterialNoteCommand> validator,
        IMaterialNoteRepository noteRepository,
        ITransactionManager transactionManager,
        UserScopedData user)
    {
        _validator = validator;
        _noteRepository = noteRepository;
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<Result<MaterialNoteResponse, Error>> Handle(
        UpsertMaterialNoteCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid userId = _user.UserId;
        Guid materialId = command.MaterialId;
        MaterialNote? note = await _noteRepository.GetByAsync(
            n => n.UserId == userId && n.MaterialId == materialId,
            cancellationToken);

        if (note is not null)
        {
            UnitResult<Error> updateResult = note.UpdateContent(command.Content);
            if (updateResult.IsFailure)
            {
                return updateResult.Error;
            }
        }
        else
        {
            Result<MaterialNote, Error> createResult = MaterialNote.Create(
                _user.UserId,
                command.MaterialId,
                command.Content);
            if (createResult.IsFailure)
            {
                return createResult.Error;
            }

            note = createResult.Value;
            await _noteRepository.AddAsync(note, cancellationToken);
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        return new MaterialNoteResponse(note.MaterialId, note.Content, note.CreatedAt, note.UpdatedAt);
    }
}
