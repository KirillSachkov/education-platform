using System.Data.Common;
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

namespace AccessService.Core.Features.Admin;

public sealed record GetActiveGrantsByUsersRequest(IReadOnlyList<Guid> UserIds);

public sealed record GetActiveGrantsByUsersQuery(IReadOnlyList<Guid> UserIds) : IQuery;

public sealed record AdminActiveGrantSummary
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid PlanId { get; init; }
    public string? PlanDisplayName { get; init; }
    public string? PlanTier { get; init; }
    public Guid PlanAuthorId { get; init; }
    public string Source { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
}

public sealed record AdminActiveGrantsByUsersResponse(
    IReadOnlyDictionary<Guid, IReadOnlyList<AdminActiveGrantSummary>> Grants);

public sealed class GetActiveGrantsByUsersValidator : AbstractValidator<GetActiveGrantsByUsersQuery>
{
    public const int MaxUserIds = 200;

    public GetActiveGrantsByUsersValidator()
    {
        RuleFor(q => q.UserIds)
            .NotEmpty()
            .WithError(Error.Validation("grants.by_users.empty", "Список пользователей не может быть пустым."));

        RuleFor(q => q.UserIds.Count)
            .LessThanOrEqualTo(MaxUserIds)
            .WithError(Error.Validation(
                "grants.by_users.too_many",
                $"За один запрос — не больше {MaxUserIds} пользователей."));
    }
}

public sealed class GetActiveGrantsByUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/access/admin/grants/by-users/",
                async Task<EndpointResult<AdminActiveGrantsByUsersResponse>> (
                    [FromBody] GetActiveGrantsByUsersRequest request,
                    [FromServices] GetActiveGrantsByUsersHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetActiveGrantsByUsersQuery(request.UserIds ?? []), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}

public sealed class GetActiveGrantsByUsersHandler
    : IQueryHandlerWithResult<AdminActiveGrantsByUsersResponse, GetActiveGrantsByUsersQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetActiveGrantsByUsersQuery> _validator;

    public GetActiveGrantsByUsersHandler(
        ITransactionManager transactionManager,
        IValidator<GetActiveGrantsByUsersQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<AdminActiveGrantsByUsersResponse, Error>> Handle(
        GetActiveGrantsByUsersQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // Dapper's snake_case ↔ PascalCase mapping is enabled in
        // AccessService.Infrastructure.Postgres registration. ORDER BY plan tier
        // first so the "most valuable" grant (FULL_ALL > LEARN_ALL > COURSE >
        // SUBSCRIPTION > FREE) renders at the top of the badge stack.
        const string sql = """
            SELECT
                g.id,
                g.user_id,
                g.plan_id,
                p.display_name AS plan_display_name,
                p.tier AS plan_tier,
                p.author_id AS plan_author_id,
                g.source,
                g.expires_at
            FROM plan_grants g
            LEFT JOIN plans p ON p.id = g.plan_id
            WHERE g.user_id = ANY(@UserIds) AND g.status = 'ACTIVE'
            ORDER BY
                CASE p.tier
                    WHEN 'FULL_ALL' THEN 0
                    WHEN 'LEARN_ALL' THEN 1
                    WHEN 'COURSE' THEN 2
                    WHEN 'SUBSCRIPTION' THEN 3
                    WHEN 'FREE' THEN 4
                    ELSE 5
                END,
                g.granted_at DESC;
            """;

        IEnumerable<AdminActiveGrantSummary> rows =
            await connection.QueryAsync<AdminActiveGrantSummary>(sql, new { UserIds = query.UserIds.ToArray() });

        Dictionary<Guid, IReadOnlyList<AdminActiveGrantSummary>> grouped = rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AdminActiveGrantSummary>)g.ToList());

        return new AdminActiveGrantsByUsersResponse(grouped);
    }
}
