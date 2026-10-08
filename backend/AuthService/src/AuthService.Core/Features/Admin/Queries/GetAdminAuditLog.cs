using System.Data.Common;
using System.Text;
using AuthService.Contracts.Admin;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Admin.Queries;

public sealed record GetAdminAuditLogQuery(
    Guid? TargetUserId,
    Guid? AdminId,
    string? Action,
    DateTime? From,
    DateTime? To,
    string? Cursor,
    int PageSize) : IQuery;

public sealed class GetAdminAuditLogQueryValidator : AbstractValidator<GetAdminAuditLogQuery>
{
    public GetAdminAuditLogQueryValidator()
    {
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 200)
            .WithError(GeneralErrors.ValueIsInvalid("pageSize"));
    }
}

public sealed class GetAdminAuditLogEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/admin/audit-log", async Task<EndpointResult<AdminAuditLogPageResponse>> (
                    [FromQuery] Guid? targetUserId,
                    [FromQuery] Guid? adminId,
                    [FromQuery] string? action,
                    [FromQuery] DateTime? from,
                    [FromQuery] DateTime? to,
                    [FromQuery] string? cursor,
                    [FromQuery] int? pageSize,
                    [FromServices] GetAdminAuditLogHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(
                    new GetAdminAuditLogQuery(
                        targetUserId, adminId, action, from, to, cursor, pageSize ?? 50),
                    ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE);
}

public sealed class GetAdminAuditLogHandler : IQueryHandlerWithResult<AdminAuditLogPageResponse, GetAdminAuditLogQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetAdminAuditLogQuery> _validator;

    public GetAdminAuditLogHandler(
        ITransactionManager transactionManager,
        IValidator<GetAdminAuditLogQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<AdminAuditLogPageResponse, Error>> Handle(
        GetAdminAuditLogQuery query,
        CancellationToken cancellationToken)
    {
        FluentValidation.Results.ValidationResult validation =
            await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        AuditCursor? cursor = AuditCursor.Decode(query.Cursor);

        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int fetchLimit = pageSize + 1;

        StringBuilder sql = new();
        sql.AppendLine("""
            SELECT
                al.id, al.admin_id, al.target_user_id, al.action,
                al.method, al.path, al.payload_json, al.result, al.error_message,
                al.created_at, al.ip_address, al.user_agent,
                admin_user.user_name AS admin_username,
                admin_user.display_name AS admin_display_name
            FROM admin_audit_log al
            LEFT JOIN users admin_user ON admin_user.id = al.admin_id
            WHERE 1 = 1
            """);

        if (query.TargetUserId is not null)
            sql.AppendLine("  AND al.target_user_id = @TargetUserId");
        if (query.AdminId is not null)
            sql.AppendLine("  AND al.admin_id = @AdminId");
        if (!string.IsNullOrWhiteSpace(query.Action))
            sql.AppendLine("  AND al.action = @Action");
        if (query.From is not null)
            sql.AppendLine("  AND al.created_at >= @From");
        if (query.To is not null)
            sql.AppendLine("  AND al.created_at < @To");
        if (cursor is not null)
            sql.AppendLine("  AND (al.created_at, al.id) < (@CursorCreatedAt, @CursorId)");

        sql.AppendLine("ORDER BY al.created_at DESC, al.id DESC");
        sql.AppendLine("LIMIT @Limit");

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<AuditLogRow> rows = await connection.QueryAsync<AuditLogRow>(
            sql.ToString(),
            new
            {
                TargetUserId = query.TargetUserId,
                AdminId = query.AdminId,
                Action = query.Action,
                From = query.From,
                To = query.To,
                Limit = fetchLimit,
                CursorCreatedAt = cursor?.CreatedAt,
                CursorId = cursor?.LastId,
            });

        List<AuditLogRow> list = rows.ToList();

        bool hasMore = list.Count > pageSize;
        if (hasMore)
            list.RemoveAt(list.Count - 1);

        List<AdminAuditLogEntryDto> items = list.Select(r => new AdminAuditLogEntryDto(
            r.Id,
            r.AdminId,
            r.AdminUsername,
            r.AdminDisplayName,
            r.TargetUserId,
            r.Action,
            r.Method,
            r.Path,
            r.PayloadJson,
            r.Result,
            r.ErrorMessage,
            r.CreatedAt,
            r.IpAddress,
            r.UserAgent)).ToList();

        string? nextCursor = hasMore && list.Count > 0
            ? AuditCursor.Encode(DateTime.SpecifyKind(list[^1].CreatedAt, DateTimeKind.Utc), list[^1].Id)
            : null;

        return new AdminAuditLogPageResponse(items, nextCursor);
    }

    internal sealed record AuditLogRow
    {
        public Guid Id { get; init; }
        public Guid AdminId { get; init; }
        public Guid? TargetUserId { get; init; }
        public string Action { get; init; } = null!;
        public string Method { get; init; } = null!;
        public string Path { get; init; } = null!;
        public string? PayloadJson { get; init; }
        public string Result { get; init; } = null!;
        public string? ErrorMessage { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? IpAddress { get; init; }
        public string? UserAgent { get; init; }
        public string? AdminUsername { get; init; }
        public string? AdminDisplayName { get; init; }
    }
}

internal sealed record AuditCursor(DateTime CreatedAt, Guid LastId)
{
    public static string Encode(DateTime createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{createdAt:O}|{id}"));

    public static AuditCursor? Decode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            string[] parts = decoded.Split('|');
            if (parts.Length != 2)
                return null;

            if (!DateTime.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime ts))
                return null;
            if (!Guid.TryParse(parts[1], out Guid id))
                return null;

            return new AuditCursor(DateTime.SpecifyKind(ts, DateTimeKind.Utc), id);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
