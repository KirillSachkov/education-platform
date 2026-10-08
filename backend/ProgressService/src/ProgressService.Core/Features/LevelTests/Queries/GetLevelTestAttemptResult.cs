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
using ProgressService.Core.Abstractions;
using ProgressService.Core.Features.Courses.Queries;
using ProgressService.Domain;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Core.Features.LevelTests.Queries;

public sealed record GetLevelTestAttemptResultQuery(Guid AttemptId) : IQuery;

public sealed class GetLevelTestAttemptResultQueryValidator : AbstractValidator<GetLevelTestAttemptResultQuery>
{
    public GetLevelTestAttemptResultQueryValidator()
    {
        RuleFor(x => x.AttemptId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetLevelTestAttemptResultQuery.AttemptId)));
    }
}

public sealed class GetLevelTestAttemptResultEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Анонимный доступ намеренно: незаклеймленная попытка отдаёт ТОЛЬКО тизер
        // (capability = знание v7 attemptId), полный разбор — после клейма и только
        // владельцу. Rate-limit — общая anonymous-read policy сервиса.
        app.MapGet("/progress/level-test/attempts/{attemptId:guid}/result",
                async Task<EndpointResult<object>> (
                    [FromRoute] Guid attemptId,
                    [FromServices] GetLevelTestAttemptResultHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetLevelTestAttemptResultQuery(attemptId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(GetCoursePublicStatsEndpoint.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

/// <summary>
///     Результат level-test попытки с lead-gate shaping'ом (ST-4, #479):
///     неклеймленная попытка (<c>UserId IS NULL</c>) → тизер любому держателю attemptId;
///     заклеймленная → полный разбор только владельцу (или админу), остальным — 401/403.
///     Правильные ответы не хранятся и не отдаются — только флаги/баллы.
/// </summary>
public sealed class GetLevelTestAttemptResultHandler
    : IQueryHandlerWithResult<object, GetLevelTestAttemptResultQuery>
{
    private readonly IValidator<GetLevelTestAttemptResultQuery> _validator;
    private readonly ILevelTestAttemptRepository _levelTestAttemptRepository;
    private readonly UserScopedData _user;

    public GetLevelTestAttemptResultHandler(
        IValidator<GetLevelTestAttemptResultQuery> validator,
        ILevelTestAttemptRepository levelTestAttemptRepository,
        UserScopedData user)
    {
        _validator = validator;
        _levelTestAttemptRepository = levelTestAttemptRepository;
        _user = user;
    }

    public async Task<Result<object, Error>> Handle(
        GetLevelTestAttemptResultQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        LevelTestAttempt? attempt = await _levelTestAttemptRepository.GetByAsync(
            a => a.Id == query.AttemptId,
            cancellationToken);
        if (attempt is null)
        {
            return ProgressErrors.LevelTestAttemptNotFound();
        }

        if (attempt.UserId is null)
        {
            return LevelTestResultMapper.BuildTeaser(attempt);
        }

        if (!_user.IsAuthenticated)
        {
            return ProgressErrors.LevelTestAttemptResultAuthenticationRequired();
        }

        if (_user.UserId != attempt.UserId && !_user.IsAdmin)
        {
            return ProgressErrors.LevelTestAttemptResultAccessDenied();
        }

        return LevelTestResultMapper.BuildFullResult(attempt);
    }
}
