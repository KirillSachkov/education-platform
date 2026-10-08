using System.Data.Common;
using AuthService.Contracts.Admin;
using AuthService.Core.Features.Auth.Telegram;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Admin.Queries;

public sealed record GetUsersQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Role,
    string? Cursor,
    DateTime? CreatedAfter = null,
    DateTime? CreatedBefore = null,
    string? Status = null,
    DateTime? LastLoginAfter = null,
    DateTime? LastLoginBefore = null) : IQuery;

public sealed record GetUsersResponse(
    IReadOnlyList<AdminUserSummaryDto> Items,
    string? NextCursor,
    // DEPRECATED: use cursor pagination (NextCursor)
    int TotalCount,
    // DEPRECATED: use cursor pagination (NextCursor)
    int Page,
    // DEPRECATED: use cursor pagination (NextCursor)
    int PageSize,
    // DEPRECATED: use cursor pagination (NextCursor)
    int TotalPages);

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithError(GeneralErrors.ValueIsInvalid("page"));

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithError(GeneralErrors.ValueIsInvalid("pageSize"));

        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class GetUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/", async Task<EndpointResult<GetUsersResponse>> (
                    [Microsoft.AspNetCore.Mvc.FromQuery] int? page,
                    [Microsoft.AspNetCore.Mvc.FromQuery] int? pageSize,
                    [Microsoft.AspNetCore.Mvc.FromQuery] string? search,
                    [Microsoft.AspNetCore.Mvc.FromQuery] string? role,
                    [Microsoft.AspNetCore.Mvc.FromQuery] string? cursor,
                    [Microsoft.AspNetCore.Mvc.FromQuery] DateTime? createdAfter,
                    [Microsoft.AspNetCore.Mvc.FromQuery] DateTime? createdBefore,
                    [Microsoft.AspNetCore.Mvc.FromQuery] string? status,
                    [Microsoft.AspNetCore.Mvc.FromQuery] DateTime? lastLoginAfter,
                    [Microsoft.AspNetCore.Mvc.FromQuery] DateTime? lastLoginBefore,
                    [Microsoft.AspNetCore.Mvc.FromServices] GetUsersHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(
                    new GetUsersQuery(
                        page ?? 1, pageSize ?? 20, search, role, cursor,
                        createdAfter, createdBefore, status,
                        lastLoginAfter, lastLoginBefore),
                    ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

public sealed class GetUsersHandler : IQueryHandlerWithResult<GetUsersResponse, GetUsersQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetUsersQuery> _validator;

    public GetUsersHandler(
        ITransactionManager transactionManager,
        IValidator<GetUsersQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<GetUsersResponse, Error>> Handle(
        GetUsersQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        string? search = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : query.Search.Trim();

        Cursor? cursor = Cursor.Decode(query.Cursor);
        bool useCursor = cursor is not null;

        // When cursor is present, page is ignored (cursor-based keyset pagination).
        // Otherwise fall back to offset for backwards compat with the deprecated page param.
        int offset = useCursor ? 0 : (page - 1) * pageSize;

        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               u.id AS id,
                               u.user_name AS user_name,
                               u.display_name AS display_name,
                               u.email AS email,
                               u.email_confirmed AS email_confirmed,
                               (u.lockout_end IS NOT NULL AND u.lockout_end > NOW()) AS is_locked_out,
                               u.created_at,
                               u.last_login_at,
                               COALESCE(
                                   (SELECT json_agg(r.name ORDER BY r.name)::text
                                    FROM user_roles ur
                                    JOIN roles r ON r.id = ur.role_id
                                    WHERE ur.user_id = u.id), '[]'
                               ) AS roles_json,
                               up.avatar_id,
                               CAST(COUNT(*) OVER() AS integer) AS total_count
                           FROM users u
                           LEFT JOIN user_profiles up ON up.id = u.id
                           WHERE
                               (@Search IS NULL OR u.user_name ILIKE '%' || @Search || '%'
                                   OR u.display_name ILIKE '%' || @Search || '%'
                                   OR EXISTS (
                                       SELECT 1 FROM user_logins tg
                                       WHERE tg.user_id = u.id
                                           AND tg.login_provider = @TgProvider
                                           AND tg.provider_display_name ILIKE '%' || @Search || '%'))
                               AND (@Role IS NULL OR EXISTS (
                                   SELECT 1 FROM user_roles ur2
                                   JOIN roles r2 ON r2.id = ur2.role_id
                                   WHERE ur2.user_id = u.id AND r2.name = @Role))
                               AND (@CreatedAfter::timestamptz IS NULL OR u.created_at >= @CreatedAfter)
                               AND (@CreatedBefore::timestamptz IS NULL OR u.created_at < @CreatedBefore)
                               AND (@LastLoginAfter::timestamptz IS NULL OR u.last_login_at >= @LastLoginAfter)
                               AND (@LastLoginBefore::timestamptz IS NULL OR u.last_login_at < @LastLoginBefore)
                               AND (
                                   @Status::text IS NULL
                                   OR (@Status = 'active' AND (u.lockout_end IS NULL OR u.lockout_end < NOW()) AND u.email_confirmed)
                                   OR (@Status = 'locked' AND u.lockout_end IS NOT NULL AND u.lockout_end > NOW())
                                   OR (@Status = 'unconfirmed' AND NOT u.email_confirmed)
                               )
                               AND (
                                   @CursorCreatedAt::timestamptz IS NULL
                                   OR (u.created_at, u.id) < (@CursorCreatedAt, @CursorId)
                               )
                           ORDER BY u.created_at DESC, u.id DESC
                           LIMIT @Limit OFFSET @Offset
                           """;

        // Always fetch +1 so we can emit a nextCursor even in legacy offset-mode requests.
        int fetchLimit = pageSize + 1;

        IEnumerable<UserListRow> rows = await connection.QueryAsync<UserListRow>(
            sql,
            new
            {
                Search = search,
                Role = query.Role,
                TgProvider = TelegramProviderConstants.PROVIDER_NAME,
                Limit = fetchLimit,
                Offset = offset,
                CursorCreatedAt = cursor?.CreatedAt,
                CursorId = cursor?.LastId,
                CreatedAfter = query.CreatedAfter,
                CreatedBefore = query.CreatedBefore,
                LastLoginAfter = query.LastLoginAfter,
                LastLoginBefore = query.LastLoginBefore,
                Status = query.Status,
            });

        List<UserListRow> list = rows.ToList();

        bool hasMore = list.Count > pageSize;
        if (hasMore)
        {
            list.RemoveAt(list.Count - 1);
        }

        int totalCount = list.FirstOrDefault()?.TotalCount ?? 0;
        int totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        List<AdminUserSummaryDto> items = list.Select(r => new AdminUserSummaryDto(
            r.Id,
            r.UserName,
            r.DisplayName,
            r.Email,
            r.EmailConfirmed,
            ParseRoles(r.RolesJson),
            r.IsLockedOut,
            r.CreatedAt,
            r.LastLoginAt,
            r.AvatarId)).ToList();

        string? nextCursor = hasMore && list.Count > 0
            ? Cursor.Encode(
                DateTime.SpecifyKind(list[^1].CreatedAt, DateTimeKind.Utc),
                list[^1].Id)
            : null;

        return new GetUsersResponse(items, nextCursor, totalCount, page, pageSize, totalPages);
    }

    private static IReadOnlyList<string> ParseRoles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "[]", StringComparison.Ordinal))
            return [];

        return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
}

internal sealed record UserListRow
{
    public Guid Id { get; init; }
    public string? UserName { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool IsLockedOut { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public string? RolesJson { get; init; }
    public Guid? AvatarId { get; init; }
    public int TotalCount { get; init; }
}
