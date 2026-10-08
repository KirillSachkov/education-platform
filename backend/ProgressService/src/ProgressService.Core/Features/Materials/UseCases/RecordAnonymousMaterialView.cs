using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;

namespace ProgressService.Core.Features.Materials.UseCases;

/// <summary>
///     Идемпотентно фиксирует факт просмотра материала анонимным посетителем (issue #234).
///     Кладёт строку в <c>anonymous_material_views(anonymous_id, material_id)</c>. Не публикует
///     domain/integration event'ов — просмотр анонима используется только в агрегат-счётчике
///     карточки материала.
/// </summary>
public sealed record RecordAnonymousMaterialViewCommand(Guid MaterialId, string AnonymousId) : ICommand;

public sealed class RecordAnonymousMaterialViewCommandValidator : AbstractValidator<RecordAnonymousMaterialViewCommand>
{
    public RecordAnonymousMaterialViewCommandValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RecordAnonymousMaterialViewCommand.MaterialId)));

        RuleFor(x => x.AnonymousId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RecordAnonymousMaterialViewCommand.AnonymousId)));

        RuleFor(x => x.AnonymousId)
            .Must(BeUuidLike)
            .When(x => !string.IsNullOrWhiteSpace(x.AnonymousId))
            .WithError(GeneralErrors.ValueIsInvalid(nameof(RecordAnonymousMaterialViewCommand.AnonymousId)));
    }

    private static bool BeUuidLike(string value) => Guid.TryParse(value, out _);
}

public sealed class RecordAnonymousMaterialViewEndpoint : IEndpoint
{
    public const string RATE_LIMIT_POLICY = "anonymous-material-view";

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/progress/materials/{materialId:guid}/anonymous-view",
                async Task<EndpointResult> (
                    [FromRoute] Guid materialId,
                    [FromBody] RecordAnonymousMaterialViewRequest request,
                    [FromServices] RecordAnonymousMaterialViewHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new RecordAnonymousMaterialViewCommand(materialId, request.AnonymousId),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(RATE_LIMIT_POLICY);
}

public sealed class RecordAnonymousMaterialViewHandler : ICommandHandler<RecordAnonymousMaterialViewCommand>
{
    private readonly IAnonymousMaterialViewRepository _repository;
    private readonly IValidator<RecordAnonymousMaterialViewCommand> _validator;

    public RecordAnonymousMaterialViewHandler(
        IAnonymousMaterialViewRepository repository,
        IValidator<RecordAnonymousMaterialViewCommand> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    public async Task<UnitResult<Error>> Handle(
        RecordAnonymousMaterialViewCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Нормализуем UUID к каноническому lower-case "D" виду — анти-флуд от вариаций
        // casing'а/braces. Один cookie не должен раздуть счётчик через смену casing'а.
        string anonymousId = Guid.Parse(command.AnonymousId).ToString("D", System.Globalization.CultureInfo.InvariantCulture);

        // INSERT ... ON CONFLICT DO NOTHING — идемпотентность + race-safety в одном SQL.
        // Domain event'ов не публикуем (анон-просмотр питает только счётчик), поэтому
        // outbox flush не нужен и ITransactionManager не требуется.
        await _repository.UpsertAsync(anonymousId, command.MaterialId, cancellationToken);

        return UnitResult.Success<Error>();
    }
}
