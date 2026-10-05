namespace Documented.Web.Models;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    // A one-time recovery secret stored only as a password hash.
    // It is shown to the user when first created/rotated and is required
    // for a zero-email password reset.
    public string? RecoveryCodeHash { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}
