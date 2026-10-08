using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AuthService.Core.Features.Admin.Audit;

public static class AdminAuditExtensions
{
    /// <summary>
    /// Tags an admin endpoint for inclusion in <c>auth.admin_audit_log</c>.
    /// Attaches <see cref="AdminAuditActionAttribute"/> metadata and wires up
    /// <see cref="AdminAuditEndpointFilter"/> on the route handler.
    /// </summary>
    public static RouteHandlerBuilder WithAdminAudit(this RouteHandlerBuilder builder, string action)
    {
        return builder
            .WithMetadata(new AdminAuditActionAttribute(action))
            .AddEndpointFilter<AdminAuditEndpointFilter>();
    }
}
