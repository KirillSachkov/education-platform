using System.Text.Json;
using System.Text.Json.Nodes;
using AuthService.Core.Database;
using AuthService.Domain.AdminAuditLog;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Admin.Audit;

/// <summary>
/// Records every admin endpoint call into <c>auth.admin_audit_log</c>.
/// Applied via <c>RouteHandlerBuilderExtensions.WithAdminAudit(action)</c>.
/// Audit-write failure is logged but never bubbles back to the caller —
/// failing to record an audit row must not break the operation.
/// </summary>
public sealed class AdminAuditEndpointFilter : IEndpointFilter
{
    private const string REDACTED_VALUE = "[REDACTED]";

    private static readonly JsonSerializerOptions PayloadJsonOptions = new() { WriteIndented = false };

    private static readonly string[] SensitivePropertyNameFragments =
        ["password", "token", "secret", "apiKey", "authorization"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UserScopedData _userScope;
    private readonly ILogger<AdminAuditEndpointFilter> _logger;

    public AdminAuditEndpointFilter(
        IServiceScopeFactory scopeFactory,
        UserScopedData userScope,
        ILogger<AdminAuditEndpointFilter> logger)
    {
        _scopeFactory = scopeFactory;
        _userScope = userScope;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        AdminAuditActionAttribute? attr = context.HttpContext.GetEndpoint()
            ?.Metadata
            .GetMetadata<AdminAuditActionAttribute>();

        if (attr is null)
        {
            return await next(context);
        }

        Guid adminId = _userScope.UserId;
        Guid? targetUserId = TryExtractTargetUserId(context);
        string method = context.HttpContext.Request.Method;
        string path = context.HttpContext.Request.Path.Value ?? string.Empty;
        string? payloadJson = TryCapturePayload(context);
        string ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        string ua = context.HttpContext.Request.Headers.UserAgent.ToString();

        // EndpointResult<T> sets the HTTP status code imperatively in ExecuteAsync, which runs
        // AFTER this filter returns — so we cannot check IStatusCodeHttpResult here.
        // Instead, register an OnStarting callback to capture the final status code right before
        // response headers are sent (after IResult.ExecuteAsync has set StatusCode).
        bool exceptionOccurred = false;

        context.HttpContext.Response.OnStarting(async () =>
        {
            // Exception path already wrote the audit row; skip the duplicate.
            if (exceptionOccurred)
                return;

            int statusCode = context.HttpContext.Response.StatusCode;
            string outcome = statusCode >= 400 ? AdminAuditResult.FAILURE : AdminAuditResult.SUCCESS;
            string? errorMsg = statusCode >= 400 ? $"HTTP {statusCode}" : null;

            await WriteAuditAsync(adminId, targetUserId, attr.Action, method, path, payloadJson,
                outcome, errorMsg, ip, ua, CancellationToken.None);
        });

        try
        {
            return await next(context);
        }
        catch (Exception ex)
        {
            exceptionOccurred = true;
            await WriteAuditAsync(adminId, targetUserId, attr.Action, method, path, payloadJson,
                AdminAuditResult.FAILURE, ex.Message, ip, ua, CancellationToken.None);
            throw;
        }
    }

    private async Task WriteAuditAsync(
        Guid adminId,
        Guid? targetUserId,
        string action,
        string method,
        string path,
        string? payloadJson,
        string result,
        string? errorMessage,
        string ipAddress,
        string userAgent,
        CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IAdminAuditLogWriter writer = scope.ServiceProvider.GetRequiredService<IAdminAuditLogWriter>();

            await writer.WriteAsync(
                adminId,
                targetUserId,
                action,
                method,
                path,
                payloadJson,
                result,
                errorMessage,
                string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress,
                string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
                ct);
        }
        catch (Exception ex)
        {
            // Audit failure must never break the operation. Log and move on.
            _logger.LogError(ex, "Failed to write admin audit log entry for action {Action}", action);
        }
    }

    private static Guid? TryExtractTargetUserId(EndpointFilterInvocationContext context)
    {
        if (context.HttpContext.Request.RouteValues.TryGetValue("userId", out object? raw)
            && raw is string s
            && Guid.TryParse(s, out Guid id))
        {
            return id;
        }

        return null;
    }

    private static string? TryCapturePayload(EndpointFilterInvocationContext context)
    {
        if (context.Arguments.Count == 0)
        {
            return null;
        }

        // Capture request DTOs only. Endpoint handlers and other injected services may have
        // public state that must never become part of the durable audit payload.
        List<object> body = [];
        foreach (object? arg in context.Arguments)
        {
            if (arg is null) continue;
            Type type = arg.GetType();
            if (type.Namespace?.StartsWith("AuthService.Contracts.", StringComparison.Ordinal) != true) continue;
            body.Add(arg);
        }

        if (body.Count == 0) return null;

        try
        {
            JsonNode? payload = JsonSerializer.SerializeToNode(
                body.Count == 1 ? body[0] : body,
                PayloadJsonOptions);

            RedactSensitiveProperties(payload);

            return payload?.ToJsonString(PayloadJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void RedactSensitiveProperties(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach ((string propertyName, JsonNode? value) in jsonObject.ToList())
            {
                if (SensitivePropertyNameFragments.Any(fragment =>
                        propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    jsonObject[propertyName] = REDACTED_VALUE;
                    continue;
                }

                RedactSensitiveProperties(value);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (JsonNode? item in jsonArray)
            {
                RedactSensitiveProperties(item);
            }
        }
    }
}
