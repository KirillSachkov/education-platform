using System.Data.Common;
using AuthService.Contracts;
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
using AuthService.Domain;

namespace AuthService.Core.Features.InternalUsers.Queries;

public sealed record GetInternalUserByEmailQuery(string Email) : IQuery;

public sealed class GetInternalUserByEmailValidator : AbstractValidator<GetInternalUserByEmailQuery>
{
    public GetInternalUserByEmailValidator()
    {
        RuleFor(x => x.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email))
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetInternalUserByEmailQuery.Email)));
    }
}

public sealed class GetInternalUserByEmailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/internal/users/by-email", async Task<EndpointResult<AuthUserLookupDto>> (
                    [FromQuery] string email,
                    [FromServices] GetInternalUserByEmailHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetInternalUserByEmailQuery(email), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class GetInternalUserByEmailHandler
    : IQueryHandlerWithResult<AuthUserLookupDto, GetInternalUserByEmailQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetInternalUserByEmailQuery> _validator;

    public GetInternalUserByEmailHandler(
        ITransactionManager transactionManager,
        IValidator<GetInternalUserByEmailQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<AuthUserLookupDto, Error>> Handle(
        GetInternalUserByEmailQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        string normalizedEmail = query.Email.Trim().ToUpperInvariant();
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                u.id,
                u.display_name,
                u.user_name,
                u.email,
                up.avatar_id AS AvatarId
            FROM users u
            LEFT JOIN user_profiles up ON up.id = u.id
            WHERE u.normalized_email = @NormalizedEmail
            LIMIT 1
            """;

        InternalUserRow? row = await connection.QueryFirstOrDefaultAsync<InternalUserRow>(
            sql,
            new { NormalizedEmail = normalizedEmail });

        if (row is null)
        {
            return AuthErrors.UserNotFound();
        }

        return new AuthUserLookupDto(row.Id, row.DisplayName, row.UserName, row.Email, row.AvatarId);
    }

    private sealed class InternalUserRow
    {
        public Guid Id { get; init; }
        public string? DisplayName { get; init; }
        public string? UserName { get; init; }
        public string Email { get; init; } = null!;
        public Guid? AvatarId { get; init; }
    }
}
