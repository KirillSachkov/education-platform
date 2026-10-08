using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Features.Courses.Queries;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Diagnostics;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Core.Features.LevelTests.UseCases;

public sealed record ClaimLevelTestAttemptsCommand(string AnonymousId) : ICommand;

public sealed class ClaimLevelTestAttemptsCommandValidator : AbstractValidator<ClaimLevelTestAttemptsCommand>
{
    public ClaimLevelTestAttemptsCommandValidator()
    {
        RuleFor(x => x.AnonymousId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(ClaimLevelTestAttemptsCommand.AnonymousId)));

        RuleFor(x => x.AnonymousId)
            .Must(value => Guid.TryParse(value, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.AnonymousId))
            .WithError(GeneralErrors.ValueIsInvalid(nameof(ClaimLevelTestAttemptsCommand.AnonymousId)));
    }
}

public sealed class ClaimLevelTestAttemptsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/level-test/attempts/claim",
                async Task<EndpointResult<ClaimLevelTestAttemptsResponse>> (
                    ClaimLevelTestAttemptsRequest request,
                    ClaimLevelTestAttemptsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new ClaimLevelTestAttemptsCommand(request.AnonymousId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.VIEW)
            .RequireRateLimiting(GetCoursePublicStatsEndpoint.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

/// <summary>
///     Lead-gate шаг 2 (ST-4, #479): привязывает ВСЕ неклеймленные попытки level-test'а
///     данного анонимного идентификатора (cookie <c>plu_anon_id</c>) к текущему
///     пользователю — после этого владелец видит полный разбор. Попытки, уже
///     заклеймленные другими пользователями, не трогаются (фильтр <c>UserId IS NULL</c>).
///     Идемпотентен: повторный клейм находит 0 попыток.
/// </summary>
public sealed class ClaimLevelTestAttemptsHandler
    : ICommandHandler<ClaimLevelTestAttemptsResponse, ClaimLevelTestAttemptsCommand>
{
    private readonly IValidator<ClaimLevelTestAttemptsCommand> _validator;
    private readonly ILevelTestAttemptRepository _levelTestAttemptRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly ProgressMetrics _metrics;
    private readonly ILogger<ClaimLevelTestAttemptsHandler> _logger;

    public ClaimLevelTestAttemptsHandler(
        IValidator<ClaimLevelTestAttemptsCommand> validator,
        ILevelTestAttemptRepository levelTestAttemptRepository,
        ITransactionManager transactionManager,
        UserScopedData user,
        ProgressMetrics metrics,
        ILogger<ClaimLevelTestAttemptsHandler> logger)
    {
        _validator = validator;
        _levelTestAttemptRepository = levelTestAttemptRepository;
        _transactionManager = transactionManager;
        _user = user;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Result<ClaimLevelTestAttemptsResponse, Error>> Handle(
        ClaimLevelTestAttemptsCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid anonymousId = Guid.Parse(command.AnonymousId);

        IReadOnlyList<LevelTestAttempt> attempts = await _levelTestAttemptRepository.GetManyByAsync(
            a => a.AnonymousId == anonymousId && a.UserId == null,
            cancellationToken);

        foreach (LevelTestAttempt attempt in attempts)
        {
            UnitResult<Error> claimResult = attempt.Claim(_user.UserId);
            if (claimResult.IsFailure)
            {
                return claimResult.Error;
            }
        }

        if (attempts.Count > 0)
        {
            UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailure)
            {
                return saveResult.Error;
            }

            // Funnel-метрика (#482): lead-gate конверсия — анонимные попытки привязаны к юзеру.
            _metrics.IncrementLevelTestClaimed(attempts.Count);

            _logger.LogInformation(
                "Level-test attempts claimed. UserId: {UserId}, ClaimedCount: {ClaimedCount}",
                _user.UserId,
                attempts.Count);
        }

        Guid? latestAttemptId = attempts
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefault();

        return new ClaimLevelTestAttemptsResponse(attempts.Count, latestAttemptId);
    }
}
