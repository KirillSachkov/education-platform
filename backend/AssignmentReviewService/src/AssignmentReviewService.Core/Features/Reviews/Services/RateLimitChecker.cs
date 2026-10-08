using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Errors;
using Microsoft.Extensions.Options;

namespace AssignmentReviewService.Core.Features.Reviews.Services;

/// <summary>
///     Domain-level rate-limit. Защищает от бизнес-кейсов которые middleware-level
///     policy не покрывает: per-submission anti-flood (студент нажимает кнопку
///     сериями) + per-user daily cap (cost guard).
///
///     Middleware-level policy <c>ar-review-iteration</c> в Program.cs покрывает
///     burst-rate (5/min на юзера); этот класс — дальние/штучные лимиты.
/// </summary>
public sealed class RateLimitChecker
{
    private readonly IAiReviewsRepository _reviews;
    private readonly IOptions<AssignmentReviewAiOptions> _options;
    private readonly TimeProvider _timeProvider;

    public RateLimitChecker(
        IAiReviewsRepository reviews,
        IOptions<AssignmentReviewAiOptions> options,
        TimeProvider timeProvider)
    {
        _reviews = reviews;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<UnitResult<Error>> EnsureCanRunAsync(
        Guid userId,
        Guid authorId,
        Guid submissionId,
        CancellationToken ct = default)
    {
        AssignmentReviewLimits limits = _options.Value.Limits;

        int submissionIterations = await _reviews.CountIterationsForSubmissionAsync(submissionId, ct);
        if (submissionIterations >= limits.MaxIterationsPerSubmission)
        {
            return ReviewErrors.RateLimitExceeded(
                "submission",
                limits.MaxIterationsPerSubmission,
                submissionIterations);
        }

        DateTimeOffset since = _timeProvider.GetUtcNow().AddDays(-1);

        int userIterations = await _reviews.CountIterationsForUserSinceAsync(userId, since, ct);
        if (userIterations >= limits.MaxIterationsPerUserPerDay)
        {
            return ReviewErrors.RateLimitExceeded(
                "user/24h",
                limits.MaxIterationsPerUserPerDay,
                userIterations);
        }

        // Issue #327 — author-level cap. Защита от cost blowup'а на популярном
        // курсе: один автор × тысячи студентов × N retry'ев → неприемлемый
        // бюджет. 0 / negative значение в конфиге = cap отключён.
        if (limits.MaxIterationsPerAuthorPerDay > 0)
        {
            int authorIterations = await _reviews.CountIterationsForAuthorSinceAsync(authorId, since, ct);
            if (authorIterations >= limits.MaxIterationsPerAuthorPerDay)
            {
                return ReviewErrors.RateLimitExceeded(
                    "author/24h",
                    limits.MaxIterationsPerAuthorPerDay,
                    authorIterations);
            }
        }

        return UnitResult.Success<Error>();
    }
}
