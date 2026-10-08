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
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Notes;

namespace ProgressService.Core.Features.MaterialNotes.UseCases;

public sealed record DeleteMaterialNoteCommand(Guid MaterialId) : ICommand;

public sealed class DeleteMaterialNoteCommandValidator : AbstractValidator<DeleteMaterialNoteCommand>
{
    public DeleteMaterialNoteCommandValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(DeleteMaterialNoteCommand.MaterialId)));
    }
}

public sealed class DeleteMaterialNoteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/progress/materials/{materialId:guid}/note",
                async Task<EndpointResult> (
                    Guid materialId,
                    DeleteMaterialNoteHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new DeleteMaterialNoteCommand(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Удаляет личную заметку текущего пользователя к материалу. Идемпотентно:
///     заметки нет → 200 без изменений. Issue #465.
/// </summary>
public sealed class DeleteMaterialNoteHandler : ICommandHandler<DeleteMaterialNoteCommand>
{
    private readonly IValidator<DeleteMaterialNoteCommand> _validator;
    private readonly IMaterialNoteRepository _noteRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public DeleteMaterialNoteHandler(
        IValidator<DeleteMaterialNoteCommand> validator,
        IMaterialNoteRepository noteRepository,
        ITransactionManager transactionManager,
        UserScopedData user)
    {
        _validator = validator;
        _noteRepository = noteRepository;
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<UnitResult<Error>> Handle(
        DeleteMaterialNoteCommand command,
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

        if (note is null)
        {
            return UnitResult.Success<Error>();
        }

        _noteRepository.Remove(note);

        return await _transactionManager.SaveChangesAsync(cancellationToken);
    }
}
