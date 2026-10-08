using Microsoft.AspNetCore.Identity;

namespace AuthService.Domain;

public class Account : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public void UpdateDisplayName(string? displayName)
    {
        DisplayName = displayName;
        UpdatedAt = DateTime.UtcNow;
    }
}
