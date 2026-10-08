using AssignmentReviewService.Contracts.Reviews;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Diagnostics;
using AssignmentReviewService.Core.Features.Reviews.Errors;
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

namespace AssignmentReviewService.Core.Features.Reviews.UseCases;

public sealed record SubmitIterationFeedbackCommand(
    Guid IterationId,
    SubmitIterationFeedbackRequest Request) : ICommand;

public sealed class SubmitIterationFeedbackValidator
    : AbstractValidator<SubmitIterationFeedbackCommand>
{
    public SubmitIterationFeedbackValidator()
    {
        RuleFor(x => x.IterationId).NotEmpty();
        RuleFor(x => x.Request.Comment)
            .MaximumLength(AiReviewIterationFeedback.COMMENT_MAX_LENGTH);
    }
}

public sealed class SubmitIterationFeedbackEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/assignment-review/iterations/{iterationId:guid}/feedback/",
                async Task<EndpointResult> (
                    [FromRoute] Guid iterationId,
                    [FromBody] SubmitIterationFeedbackRequest request,
                    [FromServices] SubmitIterationFeedbackHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(
                        new SubmitIterationFeedbackCommand(iterationId, request), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

/// <summary>
///     Issue #327 — owner submits feedback на AI iteration (👍/👎 + optional
///     comment). Повторный submit upsert'ит существующий feedback того же юзера.
/// </summary>
public sealed class SubmitIterationFeedbackHandler
    : ICommandHandler<SubmitIterationFeedbackCommand>
{
    private readonly IAiReviewsRepository _reviews;
    private readonly IAiReviewIterationFeedbackRepository _feedback;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<SubmitIterationFeedbackCommand> _validator;
    private readonly AssignmentReviewMetrics _metrics;

    public SubmitIterationFeedbackHandler(
        IAiReviewsRepository reviews,
        IAiReviewIterationFeedbackRepository feedback,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<SubmitIterationFeedbackCommand> validator,
        AssignmentReviewMetrics metrics)
    {
        _reviews = reviews;
        _feedback = feedback;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _metrics = metrics;
    }

    public async Task<UnitResult<Error>> Handle(
        SubmitIterationFeedbackCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation =
            await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Error.Validation("review.feedback.invalid", validation.Errors[0].ErrorMessage);

        AiReviewIteration? iteration = await _reviews.GetIterationByIdAsync(command.IterationId, ct);
        if (iteration is null)
            return Error.NotFound("review.iteration.not_found",
                $"AI iteration {command.IterationId} не найден.");

        AiReview? review = await _reviews.GetByAsync(r => r.Id == iteration.AiReviewId, ct);
        if (review is null)
            return ReviewErrors.ReviewNotFound(iteration.AiReviewId);

        if (!_user.IsOwnerOrAdmin(review.UserId))
            return ReviewErrors.AccessDenied();

        AiReviewIterationFeedback? existing = await _feedback.GetByAsync(
            f => f.IterationId == command.IterationId && f.UserId == _user.UserId, ct);

        if (existing is null)
        {
            AiReviewIterationFeedback created = AiReviewIterationFeedback.Create(
                command.IterationId,
                _user.UserId,
                command.Request.IsHelpful,
                command.Request.Comment);
            await _feedback.AddAsync(created, ct);
        }
        else
        {
            existing.Update(command.Request.IsHelpful, command.Request.Comment);
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        _metrics.IncrementIterationFeedback(
            verdict: iteration.Verdict?.ToString() ?? "FAILED",
            helpful: command.Request.IsHelpful);

        return UnitResult.Success<Error>();
    }
}
