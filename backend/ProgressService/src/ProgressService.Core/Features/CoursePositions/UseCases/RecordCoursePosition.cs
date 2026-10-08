using ContentAccess;
using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.CoursePositions;

namespace ProgressService.Core.Features.CoursePositions.UseCases;

/// <summary>
/// Фиксирует факт открытия материала или задания внутри курса. Идемпотентен по паре
/// (UserId, CourseId): первый вызов создаёт запись, последующие — обновляют
/// EntityType/EntityId и OpenedAt. Используется фронтом для CTA «Продолжить курс»
/// и подсветки последней позиции в программе курса.
/// </summary>
public sealed record RecordCoursePositionCommand(
    Guid CourseId, string EntityType, Guid EntityId) : ICommand;

public sealed class RecordCoursePositionCommandValidator : AbstractValidator<RecordCoursePositionCommand>
{
    public RecordCoursePositionCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RecordCoursePositionCommand.CourseId)));

        RuleFor(x => x.EntityId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RecordCoursePositionCommand.EntityId)));

        RuleFor(x => x.EntityType)
            .Must(t =>
                string.Equals(t, CoursePosition.ENTITY_TYPE_MATERIAL, StringComparison.Ordinal)
                || string.Equals(t, CoursePosition.ENTITY_TYPE_ISSUE, StringComparison.Ordinal))
            .WithError(GeneralErrors.ValueIsInvalid(nameof(RecordCoursePositionCommand.EntityType)));
    }
}

public sealed record RecordCoursePositionRequest(string EntityType, Guid EntityId);

public sealed class RecordCoursePositionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/position",
                async Task<EndpointResult> (
                        [FromRoute] Guid courseId,
                        [FromBody] RecordCoursePositionRequest request,
                        [FromServices] RecordCoursePositionHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new RecordCoursePositionCommand(courseId, request.EntityType, request.EntityId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class RecordCoursePositionHandler : ICommandHandler<RecordCoursePositionCommand>
{
    private readonly ICoursePositionRepository _repository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IValidator<RecordCoursePositionCommand> _validator;
    private readonly UserScopedData _user;

    public RecordCoursePositionHandler(
        ICoursePositionRepository repository,
        IEntitlementChecker entitlementChecker,
        IValidator<RecordCoursePositionCommand> validator,
        UserScopedData user)
    {
        _repository = repository;
        _entitlementChecker = entitlementChecker;
        _validator = validator;
        _user = user;
    }

    public async Task<UnitResult<Error>> Handle(
        RecordCoursePositionCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid userId = _user.UserId;
        Guid courseId = command.CourseId;

        // IDOR-защита (access-derive-model Phase 2): enrollment стал ленивым progress-anchor'ом,
        // поэтому «есть ли запись на курс» больше не гейт. Таблица позиций user+course-scoped
        // (FK на enrollment нет), anchor создавать не нужно. Защита от накачки чужими courseId —
        // per-item entitlement check ниже: без grant'а на сам открываемый ресурс upsert не пройдёт.
        // Доступ к самому открываемому ресурсу. Even entitled users may not have access to a
        // specific gated item (e.g., paywalled issue) — check per-item.
        bool isIssue = string.Equals(
            command.EntityType, CoursePosition.ENTITY_TYPE_ISSUE, StringComparison.Ordinal);

        string resourceType = isIssue ? ResourceTypes.ISSUE : ResourceTypes.MATERIAL;

        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            resourceType,
            command.EntityId,
            cancellationToken);

        if (!accessDecision.IsGranted)
        {
            return isIssue
                ? ProgressErrors.IssueLockedForTrial()
                : ProgressErrors.MaterialAccessDenied();
        }

        await _repository.UpsertAsync(
            userId, courseId, command.EntityType, command.EntityId, cancellationToken);

        return UnitResult.Success<Error>();
    }
}
