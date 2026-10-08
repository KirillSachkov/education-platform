using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Core.Features.LevelTests.Queries;

public sealed record GetMyLatestLevelTestAttemptQuery : IQuery;

public sealed class GetMyLatestLevelTestAttemptEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/level-test/attempts/my-latest",
                async Task<EndpointResult<LevelTestAttemptResultResponse>> (
                    GetMyLatestLevelTestAttemptHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMyLatestLevelTestAttemptQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

/// <summary>
///     Последняя попытка level-test'а текущего пользователя (#528) — питает блок
///     «Твой результат» на лендинге воронки. Полный разбор: own-data (Tier-3 не
///     нужен), попытки нет → 404.
/// </summary>
public sealed class GetMyLatestLevelTestAttemptHandler
    : IQueryHandlerWithResult<LevelTestAttemptResultResponse, GetMyLatestLevelTestAttemptQuery>
{
    private readonly ILevelTestAttemptRepository _levelTestAttemptRepository;
    private readonly UserScopedData _user;

    public GetMyLatestLevelTestAttemptHandler(
        ILevelTestAttemptRepository levelTestAttemptRepository,
        UserScopedData user)
    {
        _levelTestAttemptRepository = levelTestAttemptRepository;
        _user = user;
    }

    public async Task<Result<LevelTestAttemptResultResponse, Error>> Handle(
        GetMyLatestLevelTestAttemptQuery query,
        CancellationToken cancellationToken)
    {
        LevelTestAttempt? attempt = await _levelTestAttemptRepository.GetLatestByUserAsync(
            _user.UserId,
            cancellationToken);
        if (attempt is null)
        {
            return ProgressErrors.LevelTestAttemptNotFound();
        }

        return LevelTestResultMapper.BuildFullResult(attempt);
    }
}
