using System.Data.Common;
using System.Globalization;
using System.Text;
using AuthService.Core.Features.Auth.Telegram;
using AuthService.Core.Features.Admin.Audit;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Admin.Queries;

public sealed class ExportUsersCsvEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/admin/export.csv", async (
                    [FromQuery] string? search,
                    [FromQuery] string? role,
                    [FromQuery] DateTime? createdAfter,
                    [FromQuery] DateTime? createdBefore,
                    [FromQuery] string? status,
                    [FromQuery] DateTime? lastLoginAfter,
                    [FromQuery] DateTime? lastLoginBefore,
                    [FromServices] ExportUsersCsvHandler handler,
                    HttpContext httpContext,
                    CancellationToken ct) =>
                await handler.WriteAsync(
                    httpContext,
                    search,
                    role,
                    createdAfter,
                    createdBefore,
                    status,
                    lastLoginAfter,
                    lastLoginBefore,
                    ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UsersExportCsv);
}

public sealed class ExportUsersCsvHandler
{
    private const int MAX_ROWS = 50_000;

    private readonly ITransactionManager _transactionManager;

    public ExportUsersCsvHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task WriteAsync(
        HttpContext httpContext,
        string? search,
        string? role,
        DateTime? createdAfter,
        DateTime? createdBefore,
        string? status,
        DateTime? lastLoginAfter,
        DateTime? lastLoginBefore,
        CancellationToken ct)
    {
        DbConnection connection = _transactionManager.GetDbConnection();
        string? normalizedSearch = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();

        const string sql = """
            SELECT
                u.id,
                u.user_name,
                u.email,
                u.display_name,
                u.email_confirmed,
                (u.lockout_end IS NOT NULL AND u.lockout_end > NOW()) AS is_locked,
                u.created_at,
                u.last_login_at,
                COALESCE(
                    (SELECT string_agg(r.name, ';' ORDER BY r.name)
                     FROM user_roles ur
                     JOIN roles r ON r.id = ur.role_id
                     WHERE ur.user_id = u.id), ''
                ) AS roles
            FROM users u
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
            ORDER BY u.created_at DESC
            LIMIT @Limit;
            """;

        httpContext.Response.ContentType = "text/csv; charset=utf-8";
        httpContext.Response.Headers.ContentDisposition =
            $"attachment; filename=\"users-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv\"";

        await using StreamWriter writer = new(
            httpContext.Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

        await writer.WriteLineAsync(
            "id,user_name,email,display_name,email_confirmed,is_locked,created_at,last_login_at,roles");

        IEnumerable<UserCsvRow> rows = await connection.QueryAsync<UserCsvRow>(sql, new
        {
            Search = normalizedSearch,
            TgProvider = TelegramProviderConstants.PROVIDER_NAME,
            Role = role,
            CreatedAfter = createdAfter,
            CreatedBefore = createdBefore,
            LastLoginAfter = lastLoginAfter,
            LastLoginBefore = lastLoginBefore,
            Status = status,
            Limit = MAX_ROWS,
        });

        foreach (UserCsvRow r in rows)
        {
            if (ct.IsCancellationRequested) break;
            await writer.WriteLineAsync(string.Join(",",
                Csv(r.Id.ToString()),
                Csv(r.UserName),
                Csv(r.Email),
                Csv(r.DisplayName),
                Csv(r.EmailConfirmed ? "true" : "false"),
                Csv(r.IsLocked ? "true" : "false"),
                Csv(r.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
                Csv(r.LastLoginAt?.ToString("O", CultureInfo.InvariantCulture)),
                Csv(r.Roles)));
        }

        await writer.FlushAsync(ct);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        ReadOnlySpan<char> trimmed = value.AsSpan().TrimStart();
        if (!trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@')
            value = "'" + value;

        bool needsQuotes = value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal);
        if (!needsQuotes) return value;
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record UserCsvRow
    {
        public Guid Id { get; init; }
        public string? UserName { get; init; }
        public string? Email { get; init; }
        public string? DisplayName { get; init; }
        public bool EmailConfirmed { get; init; }
        public bool IsLocked { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? LastLoginAt { get; init; }
        public string? Roles { get; init; }
    }
}
