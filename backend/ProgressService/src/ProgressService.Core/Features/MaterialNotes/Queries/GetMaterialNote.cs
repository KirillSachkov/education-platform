using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Notes;

namespace ProgressService.Core.Features.MaterialNotes.Queries;

public sealed record GetMaterialNoteQuery(Guid MaterialId) : IQuery;

public sealed class GetMaterialNoteQueryValidator : AbstractValidator<GetMaterialNoteQuery>
{
    public GetMaterialNoteQueryValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetMaterialNoteQuery.MaterialId)));
    }
}

public sealed class GetMaterialNoteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/materials/{materialId:guid}/note",
                async Task<EndpointResult<MaterialNoteResponse>> (
                    Guid materialId,
                    [FromServices] GetMaterialNoteHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMaterialNoteQuery(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Возвращает личную заметку текущего пользователя к материалу. Own-data:
///     Tier-3 entitlement-чек не нужен (контент материала не возвращается).
///     Заметки нет → 404 <c>material.note.not.found</c>. Issue #465.
/// </summary>
public sealed class GetMaterialNoteHandler : IQueryHandlerWithResult<MaterialNoteResponse, GetMaterialNoteQuery>
{
    private readonly IValidator<GetMaterialNoteQuery> _validator;
    private readonly IMaterialNoteRepository _noteRepository;
    private readonly UserScopedData _user;

    public GetMaterialNoteHandler(
        IValidator<GetMaterialNoteQuery> validator,
        IMaterialNoteRepository noteRepository,
        UserScopedData user)
    {
        _validator = validator;
        _noteRepository = noteRepository;
        _user = user;
    }

    public async Task<Result<MaterialNoteResponse, Error>> Handle(
        GetMaterialNoteQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid userId = _user.UserId;
        Guid materialId = query.MaterialId;
        MaterialNote? note = await _noteRepository.GetByAsync(
            n => n.UserId == userId && n.MaterialId == materialId,
            cancellationToken);

        if (note is null)
        {
            return ProgressErrors.MaterialNoteNotFound();
        }

        return new MaterialNoteResponse(note.MaterialId, note.Content, note.CreatedAt, note.UpdatedAt);
    }
}
