using System.Data.Common;
using AuthService.Contracts;
using AuthService.Core.Features.Auth.Telegram;
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

public sealed record GetInternalUsersBatchQuery(InternalUsersBatchRequest Request) : IQuery;

public sealed class GetInternalUsersBatchQueryValidator : AbstractValidator<GetInternalUsersBatchQuery>
{
    public GetInternalUsersBatchQueryValidator()
    {
        RuleFor(x => x.Request.UserIds)
            .NotNull();

        RuleFor(x => x.Request.UserIds.Count)
            .LessThanOrEqualTo(500);
    }
}

public sealed class GetInternalUsersBatchEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/internal/users/batch", async Task<EndpointResult<IReadOnlyList<AuthUserLookupDto>>> (
                    [FromBody] InternalUsersBatchRequest request,
                    [FromServices] GetInternalUsersBatchHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetInternalUsersBatchQuery(request), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class GetInternalUsersBatchHandler
    : IQueryHandlerWithResult<IReadOnlyList<AuthUserLookupDto>, GetInternalUsersBatchQuery>
{
    private readonly IValidator<GetInternalUsersBatchQuery> _validator;
    private readonly ITransactionManager _transactionManager;

    public GetInternalUsersBatchHandler(
        IValidator<GetInternalUsersBatchQuery> validator,
        ITransactionManager transactionManager)
    {
        _validator = validator;
        _transactionManager = transactionManager;
    }

    public async Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> Handle(
        GetInternalUsersBatchQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        if (query.Request.UserIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>([]);
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                u.id,
                u.display_name,
                u.user_name,
                u.email,
                up.avatar_id AS AvatarId,
                tg.provider_display_name AS TelegramUsername
            FROM users u
            LEFT JOIN user_profiles up ON up.id = u.id
            LEFT JOIN user_logins tg ON tg.user_id = u.id AND tg.login_provider = @TgProvider
            WHERE u.id = ANY(@UserIds)
            """;

        List<InternalUserRow> rows = (await connection.QueryAsync<InternalUserRow>(
            sql,
            new
            {
                UserIds = query.Request.UserIds.ToArray(),
                TgProvider = TelegramProviderConstants.PROVIDER_NAME,
            })).ToList();

        List<AuthUserLookupDto> users = rows
            .Select(row => new AuthUserLookupDto(
                row.Id, row.DisplayName, row.UserName, row.Email, row.AvatarId, row.TelegramUsername))
            .ToList();

        return users;
    }

    private sealed class InternalUserRow
    {
        public Guid Id { get; init; }
        public string? DisplayName { get; init; }
        public string? UserName { get; init; }
        public string Email { get; init; } = null!;
        public Guid? AvatarId { get; init; }
        public string? TelegramUsername { get; init; }
    }
}
