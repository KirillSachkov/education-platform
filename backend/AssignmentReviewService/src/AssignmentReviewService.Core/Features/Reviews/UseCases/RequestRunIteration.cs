using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Errors;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Domain.Reviews;
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

namespace AssignmentReviewService.Core.Features.Reviews.UseCases;

/// <summary>
///     Response от <c>POST /run-iteration/</c>: подтверждение, что итерация
///     поставлена в очередь и будет выполнена в background-handler'е
///     (<see cref="RunAiReviewRequestedHandler"/>). UI получает <c>aiReviewId</c>
///     и поллит <c>/by-submission/{submissionId}/</c> чтобы увидеть результат
///     (default polling — <c>aiReviewBySubmissionQueryOptions.staleTime=10s</c>).
/// </summary>
public sealed record RequestRunIterationResponse(Guid AiReviewId, string Status);

/// <summary>
///     Manual «Запустить AI-проверку» от автора курса / админа. Endpoint —
///     thin shell поверх durable outbox: проводит fast-fail проверки и публикует
///     <see cref="RunAiReviewRequested"/>, дальше работу делает Wolverine-handler.
/// </summary>
public sealed record RequestRunIterationCommand(
    Guid AiReviewId,
    string? ModelOverride = null) : ICommand;

public sealed class RequestRunIterationValidator : AbstractValidator<RequestRunIterationCommand>
{
    public RequestRunIterationValidator()
    {
        RuleFor(x => x.AiReviewId).NotEmpty();
        RuleFor(x => x.ModelOverride).MaximumLength(AiModelSlot.MAX_MODEL_LENGTH);
    }
}

public sealed class RequestRunIterationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/assignment-review/reviews/{id:guid}/run-iteration/",
                async Task<EndpointResult<RequestRunIterationResponse>> (
                    [FromRoute] Guid id,
                    [FromQuery(Name = "modelOverride")] string? modelOverride,
                    [FromServices] RequestRunIterationHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new RequestRunIterationCommand(id, modelOverride), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW)
            .RequireRateLimiting("ar-review-iteration");
    }
}

/// <summary>
///     Fast-fail проверки + publish команды в durable outbox. До #357 endpoint
///     синхронно гонял весь LLM-pipeline и при разрыве клиента отменял HTTP к
///     AITunnel посередине → <c>review.llm.unavailable</c>. Теперь heavy lifting
///     выполняется в <see cref="RunAiReviewRequestedHandler"/> в отдельном
///     Wolverine message-execution'е, не связанном с HTTP-запросом инициатора.
/// </summary>
public sealed class RequestRunIterationHandler
    : ICommandHandler<RequestRunIterationResponse, RequestRunIterationCommand>
{
    private readonly IAiReviewsRepository _reviews;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<RequestRunIterationCommand> _validator;
    private readonly IAssignmentReviewAiModelSettingsResolver _settingsResolver;
    private readonly ILogger<RequestRunIterationHandler> _logger;

    public RequestRunIterationHandler(
        IAiReviewsRepository reviews,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<RequestRunIterationCommand> validator,
        IAssignmentReviewAiModelSettingsResolver settingsResolver,
        ILogger<RequestRunIterationHandler> logger)
    {
        _reviews = reviews;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _settingsResolver = settingsResolver;
        _logger = logger;
    }

    public async Task<Result<RequestRunIterationResponse, Error>> Handle(
        RequestRunIterationCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Error.Validation("review.run_iteration.invalid", validation.Errors[0].ErrorMessage);

        // Платформенный тумблер (#355) — fail fast чтобы юзер сразу увидел
        // «AI выключен», а не получил 200 + dormant очередь.
        if (!await _settingsResolver.ResolveReviewEnabledAsync(ct))
            return ReviewErrors.ReviewDisabled();

        AiReview? review = await _reviews.GetByAsync(r => r.Id == command.AiReviewId, ct);
        if (review is null)
            return ReviewErrors.ReviewNotFound(command.AiReviewId);

        // Authz делаем тут — у Wolverine-handler'а нет request-scope'а с user'ом.
        // RunIterationHandler внутри Wolverine'а получит SystemTriggered=true и
        // пропустит этот же чек как «уже выполнено upstream».
        if (!_user.IsOwnerOrAdmin(review.AuthorId))
            return ReviewErrors.AccessDenied();

        // In-memory гард против двойного клика. RunningLock на DB-уровне сработает
        // внутри handler'а как настоящий exclusivity-gate (см. TryAcquireRunningLockAsync).
        if (review.Status == AiReviewStatus.RUNNING)
            return ReviewErrors.ReviewAlreadyRunning(review.Id);

        // ModelOverride гейтится permission'ом (Phase 11 / #281). Если у юзера нет
        // OVERRIDE_MODEL — silently drop, чтобы не палить existence фичи.
        string? effectiveOverride = !string.IsNullOrWhiteSpace(command.ModelOverride)
            && _user.HasPermission(PlatformPermissions.Ai.OVERRIDE_MODEL)
                ? command.ModelOverride!.Trim()
                : null;

        // #3: ручной запуск автором/админом использует расширенный manual cap — большой
        // PR будет нарезан на batch'и, но аварийный потолок стоимости остаётся.
        await _outbox.PublishAsync(new RunAiReviewRequested(review.Id, effectiveOverride, AllowOversizedDiff: true));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation(
            "Queued AI review {AiReviewId} for execution (modelOverride={ModelOverride}).",
            review.Id, effectiveOverride ?? "<none>");

        return new RequestRunIterationResponse(review.Id, Status: "QUEUED");
    }
}
