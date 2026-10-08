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

public sealed record SearchInternalUsersQuery(InternalUsersSearchRequest Request) : IQuery;

public sealed class SearchInternalUsersQueryValidator : AbstractValidator<SearchInternalUsersQuery>
{
    public SearchInternalUsersQueryValidator()
    {
        RuleFor(x => x.Request.Query)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(100);

        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, InternalUsersSearchRequest.MAX_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid("limit"));
    }
}

public sealed class SearchInternalUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/internal/users/search", async Task<EndpointResult<IReadOnlyList<AuthUserLookupDto>>> (
                    [FromBody] InternalUsersSearchRequest request,
                    [FromServices] SearchInternalUsersHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new SearchInternalUsersQuery(request), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class SearchInternalUsersHandler
    : IQueryHandlerWithResult<IReadOnlyList<AuthUserLookupDto>, SearchInternalUsersQuery>
{
    private readonly IValidator<SearchInternalUsersQuery> _validator;
    private readonly ITransactionManager _transactionManager;

    public SearchInternalUsersHandler(
        IValidator<SearchInternalUsersQuery> validator,
        ITransactionManager transactionManager)
    {
        _validator = validator;
        _transactionManager = transactionManager;
    }

    public async Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> Handle(
        SearchInternalUsersQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        string search = query.Request.Query.Trim();

        // ILIKE по username/display_name/telegram. Аналогично admin-search в /users/,
        // но без roles/lockout, без offset-пагинации — только prefix list для autocomplete.
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
            WHERE
                u.user_name ILIKE '%' || @Query || '%'
                OR u.display_name ILIKE '%' || @Query || '%'
                OR tg.provider_display_name ILIKE '%' || @Query || '%'
            ORDER BY u.email
            LIMIT @Limit
            """;

        List<InternalUserRow> rows = (await connection.QueryAsync<InternalUserRow>(
            sql,
            new
            {
                Query = search,
                query.Request.Limit,
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
