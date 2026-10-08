using System.Data.Common;
using AuthService.Contracts;
using AuthService.Core.Features.Auth.GitHub;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.InternalUsers.Queries;

/// <summary>
///     Internal endpoint (#532): keyset-страница id всех НЕ залоченных пользователей.
///     Consumer — NotificationService.WeeklyDigestRunner (платформенный еженедельный
///     дайджест всем зарегистрированным). Только id — без email/имени, ПДн не утекают;
///     email резолвится точечно EmailNotificationChannel'ом при доставке.
///     <para>
///         <paramref name="GithubLinked"/> (#699, epic #696): опциональный фильтр по наличию
///         GitHub-привязки (<c>user_logins.login_provider = 'GitHub'</c>). <c>true</c> — только
///         привязанные, <c>false</c> — только без привязки, <c>null</c> — все. Consumer —
///         кампании NotificationService «вход теперь по почте» / «привяжи аккаунты».
///     </para>
/// </summary>
public sealed record GetAllUserIdsQuery(Guid AfterId, int Limit, bool? GithubLinked) : IQuery;

public sealed class GetAllUserIdsValidator : AbstractValidator<GetAllUserIdsQuery>
{
    public const int MAX_LIMIT = 1000;

    public GetAllUserIdsValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, MAX_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetAllUserIdsQuery.Limit)));
    }
}

public sealed class GetAllUserIdsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/internal/users/ids", async Task<EndpointResult<AllUserIdsResponse>> (
                    [FromQuery] Guid? afterId,
                    [FromQuery] int? limit,
                    [FromQuery] bool? githubLinked,
                    GetAllUserIdsHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(
                    new GetAllUserIdsQuery(
                        afterId ?? Guid.Empty,
                        limit ?? GetAllUserIdsHandler.DEFAULT_LIMIT,
                        githubLinked),
                    ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class GetAllUserIdsHandler
    : IQueryHandlerWithResult<AllUserIdsResponse, GetAllUserIdsQuery>
{
    public const int DEFAULT_LIMIT = 500;

    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetAllUserIdsQuery> _validator;

    public GetAllUserIdsHandler(
        ITransactionManager transactionManager,
        IValidator<GetAllUserIdsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<AllUserIdsResponse, Error>> Handle(
        GetAllUserIdsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Фильтр аудитории по GitHub-привязке (#699) — та же идиома EXISTS user_logins,
        // что в GetMyProfile/GetUserGithubStatus.
        string githubFilter = query.GithubLinked switch
        {
            true => """
                      AND EXISTS (SELECT 1 FROM user_logins ul
                                  WHERE ul.user_id = u.id AND ul.login_provider = @GithubProvider)
                    """,
            false => """
                       AND NOT EXISTS (SELECT 1 FROM user_logins ul
                                       WHERE ul.user_id = u.id AND ul.login_provider = @GithubProvider)
                     """,
            null => string.Empty,
        };

        // Та же идиома locked, что в admin-stats/export: lockout_end в будущем = залочен.
        string sql = $"""
                      SELECT u.id
                      FROM users u
                      WHERE u.id > @AfterId
                        AND (u.lockout_end IS NULL OR u.lockout_end <= NOW())
                      {githubFilter}
                      ORDER BY u.id
                      LIMIT @Limit;
                      """;

        DbConnection connection = _transactionManager.GetDbConnection();

        object parameters = query.GithubLinked is null
            ? new { query.AfterId, query.Limit }
            : new { query.AfterId, query.Limit, GithubProvider = GitHubRoutes.PROVIDER_NAME };

        IReadOnlyList<Guid> ids = (await connection.QueryAsync<Guid>(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken))).ToList();

        Guid? nextAfterId = ids.Count == query.Limit ? ids[^1] : null;

        return new AllUserIdsResponse(ids, nextAfterId);
    }
}
