using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Materials.UseCases;

public sealed record UpdateMaterialContentCommand(
    Guid MaterialId,
    Guid GenerationJobId,
    Guid VideoId,
    Guid AssetVersion,
    string Markdown) : ICommand;

public sealed class UpdateMaterialContentValidator : AbstractValidator<UpdateMaterialContentCommand>
{
    public UpdateMaterialContentValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateMaterialContentCommand.MaterialId)));

        RuleFor(x => x.GenerationJobId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateMaterialContentCommand.GenerationJobId)));

        RuleFor(x => x.VideoId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateMaterialContentCommand.VideoId)));

        RuleFor(x => x.AssetVersion)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateMaterialContentCommand.AssetVersion)));

        RuleFor(x => x.Markdown)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateMaterialContentCommand.Markdown)));
    }
}

public sealed class UpdateMaterialContentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/internal/materials/{materialId:guid}/content/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid materialId,
                [FromBody] UpdateMaterialContentRequest request,
                [FromServices] ICommandHandler<Guid, UpdateMaterialContentCommand> handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new UpdateMaterialContentCommand(
                        materialId,
                        request.GenerationJobId,
                        request.VideoId,
                        request.AssetVersion,
                        request.Markdown),
                    cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class UpdateMaterialContentHandler : ICommandHandler<Guid, UpdateMaterialContentCommand>
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<UpdateMaterialContentCommand> _validator;

    public UpdateMaterialContentHandler(
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<UpdateMaterialContentCommand> validator)
    {
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateMaterialContentCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Material, Error> materialResult = await _materialsRepository.GetByAsync(
            material => material.Id == command.MaterialId,
            cancellationToken);
        if (materialResult.IsFailure)
            return materialResult.Error;

        Material material = materialResult.Value;

        if (material.VideoId?.Value != command.VideoId)
        {
            return Error.Validation(
                "material.content.video_mismatch",
                "Материал больше не привязан к этому видео");
        }

        Result<MarkdownContent, Error> contentResult = MarkdownContent.Create(command.Markdown);
        if (contentResult.IsFailure)
            return contentResult.Error;

        material.SetContent(contentResult.Value);

        // Без MaterialUpdated event SearchService не получает уведомление об
        // изменении content'а → конспект, сгенерированный AI pipeline'ом
        // (MPS → ECS UpdateMaterialContent), оставался stale в Typesense.
        await _outbox.PublishAsync(new MaterialUpdated(material.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return material.Id;
    }
}
