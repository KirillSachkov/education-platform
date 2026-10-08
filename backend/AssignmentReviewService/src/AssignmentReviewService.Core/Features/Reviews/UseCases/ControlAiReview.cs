using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Errors;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Domain.Reviews;
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
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace AssignmentReviewService.Core.Features.Reviews.UseCases;

public sealed record AiReviewControlResponse(Guid AiReviewId, string Status);

public sealed record CancelActiveAiReviewsResponse(int CancelledCount);

public sealed record RestartAiReviewCommand(Guid AiReviewId) : ICommand;

public sealed record CancelAiReviewCommand(Guid AiReviewId) : ICommand;

public sealed record CancelActiveAiReviewsCommand : ICommand;

public sealed class RestartAiReviewValidator : AbstractValidator<RestartAiReviewCommand>
{
    public RestartAiReviewValidator()
    {
        RuleFor(x => x.AiReviewId).NotEmpty();
    }
}

public sealed class CancelAiReviewValidator : AbstractValidator<CancelAiReviewCommand>
{
    public CancelAiReviewValidator()
    {
        RuleFor(x => x.AiReviewId).NotEmpty();
    }
}

public sealed class ControlAiReviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/assignment-review/reviews/{id:guid}/restart/",
                async Task<EndpointResult<AiReviewControlResponse>> (
                    [FromRoute] Guid id,
                    [FromServices] RestartAiReviewHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new RestartAiReviewCommand(id), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);

        app.MapPost("/assignment-review/reviews/{id:guid}/cancel/",
                async Task<EndpointResult<AiReviewControlResponse>> (
                    [FromRoute] Guid id,
                    [FromServices] CancelAiReviewHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new CancelAiReviewCommand(id), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);

        app.MapPost("/assignment-review/admin/reviews/active/cancel/",
                async Task<EndpointResult<CancelActiveAiReviewsResponse>> (
                    [FromServices] CancelActiveAiReviewsHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new CancelActiveAiReviewsCommand(), ct))
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }
}

public sealed class RestartAiReviewHandler
    : ICommandHandler<AiReviewControlResponse, RestartAiReviewCommand>
{
    private readonly IAiReviewsRepository _reviews;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<RestartAiReviewCommand> _validator;
    private readonly ILogger<RestartAiReviewHandler> _logger;

    public RestartAiReviewHandler(
        IAiReviewsRepository reviews,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<RestartAiReviewCommand> validator,
        ILogger<RestartAiReviewHandler> logger)
    {
        _reviews = reviews;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<AiReviewControlResponse, Error>> Handle(
        RestartAiReviewCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Error.Validation("review.restart.invalid", validation.Errors[0].ErrorMessage);

        AiReview? review = await _reviews.GetByAsync(r => r.Id == command.AiReviewId, ct);
        if (review is null)
            return ReviewErrors.ReviewNotFound(command.AiReviewId);

        if (!_user.IsOwnerOrAdmin(review.AuthorId))
            return ReviewErrors.AccessDenied();

        review.ResetForRestart(DateTimeOffset.UtcNow);
        await _outbox.PublishAsync(new RunAiReviewRequested(
            review.Id,
            ModelOverride: null,
            AllowOversizedDiff: true,
            ForceFresh: true));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation("AI review {AiReviewId} restarted by user {UserId}.", review.Id, _user.UserId);
        return new AiReviewControlResponse(review.Id, review.Status.ToString());
    }
}

public sealed class CancelAiReviewHandler
    : ICommandHandler<AiReviewControlResponse, CancelAiReviewCommand>
{
    private readonly IAiReviewsRepository _reviews;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _user;
    private readonly IValidator<CancelAiReviewCommand> _validator;

    public CancelAiReviewHandler(
        IAiReviewsRepository reviews,
        ITransactionManager transactions,
        IOutboxService outbox,
        UserScopedData user,
        IValidator<CancelAiReviewCommand> validator)
    {
        _reviews = reviews;
        _transactions = transactions;
        _outbox = outbox;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<AiReviewControlResponse, Error>> Handle(
        CancelAiReviewCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Error.Validation("review.cancel.invalid", validation.Errors[0].ErrorMessage);

        AiReview? review = await _reviews.GetByAsync(r => r.Id == command.AiReviewId, ct);
        if (review is null)
            return ReviewErrors.ReviewNotFound(command.AiReviewId);

        if (!_user.IsOwnerOrAdmin(review.AuthorId))
            return ReviewErrors.AccessDenied();

        if (review.Status is not (AiReviewStatus.QUEUED or AiReviewStatus.RUNNING))
            return new AiReviewControlResponse(review.Id, review.Status.ToString());

        AiReviewIteration iteration = review.StartCancelledIteration("review.cancelled", "manual-control");
        UnitResult<Error> iterationSave = await _transactions.SaveChangesAsync(ct);
        if (iterationSave.IsFailure)
            return iterationSave.Error;

        review.OnIterationFailed(iteration);
        await PublishCancelledAsync(review, iteration);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        return new AiReviewControlResponse(review.Id, review.Status.ToString());
    }

    private async Task PublishCancelledAsync(AiReview review, AiReviewIteration iteration) =>
        await _outbox.PublishAsync(new AiReviewIterationCompleted(
            review.Id,
            iteration.Id,
            review.SubmissionId,
            review.UserId,
            review.IssueId,
            iteration.IterationNumber,
            string.Empty,
            null,
            iteration.CompletedAt!.Value));
}

public sealed class CancelActiveAiReviewsHandler
    : ICommandHandler<CancelActiveAiReviewsResponse, CancelActiveAiReviewsCommand>
{
    private readonly IAiReviewsRepository _reviews;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;

    public CancelActiveAiReviewsHandler(
        IAiReviewsRepository reviews,
        ITransactionManager transactions,
        IOutboxService outbox)
    {
        _reviews = reviews;
        _transactions = transactions;
        _outbox = outbox;
    }

    public async Task<Result<CancelActiveAiReviewsResponse, Error>> Handle(
        CancelActiveAiReviewsCommand command, CancellationToken ct)
    {
        IReadOnlyList<AiReview> activeReviews = await _reviews.ListActiveAsync(ct);
        if (activeReviews.Count == 0)
            return new CancelActiveAiReviewsResponse(0);

        List<(AiReview Review, AiReviewIteration Iteration)> cancelled = [];
        foreach (AiReview review in activeReviews)
        {
            AiReviewIteration iteration = review.StartCancelledIteration("review.cancelled", "admin-bulk-cancel");
            cancelled.Add((review, iteration));
        }

        UnitResult<Error> iterationSave = await _transactions.SaveChangesAsync(ct);
        if (iterationSave.IsFailure)
            return iterationSave.Error;

        foreach ((AiReview review, AiReviewIteration iteration) in cancelled)
        {
            review.OnIterationFailed(iteration);
            await _outbox.PublishAsync(new AiReviewIterationCompleted(
                review.Id,
                iteration.Id,
                review.SubmissionId,
                review.UserId,
                review.IssueId,
                iteration.IterationNumber,
                string.Empty,
                null,
                iteration.CompletedAt!.Value));
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        return new CancelActiveAiReviewsResponse(cancelled.Count);
    }
}
