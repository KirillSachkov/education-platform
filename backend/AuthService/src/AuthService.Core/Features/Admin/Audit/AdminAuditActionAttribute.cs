namespace AuthService.Core.Features.Admin.Audit;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AdminAuditActionAttribute : Attribute
{
    public string Action { get; }

    public AdminAuditActionAttribute(string action)
    {
        Action = action;
    }
}
