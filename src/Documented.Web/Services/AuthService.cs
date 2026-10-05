using System.Security.Claims;
using System.Security.Cryptography;
using Documented.Web.Data;
using Documented.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Services;

public sealed class AuthService(AppDbContext db, IHttpContextAccessor http)
{
    private readonly PasswordHasher<AppUser> passwordHasher = new();
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<(bool Success, string? Error, AppUser? User, string? RecoveryCode)> RegisterAsync(
        string businessName,
        string email,
        string password)
    {
        businessName = (businessName ?? string.Empty).Trim();
        email = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (businessName.Length < 2)
            return (false, "Business name is required.", null, null);

        if (email.Length < 5 || !email.Contains('@'))
            return (false, "Enter a valid email address.", null, null);

        if (password is null || password.Length < 8)
            return (false, "Password must contain at least 8 characters.", null, null);

        if (await db.Users.AnyAsync(x => x.Email == email))
            return (false, "An account with this email already exists.", null, null);

        var slugBase = MakeSlug(businessName);
        var slug = slugBase;
        var suffix = 2;
        while (await db.Tenants.AnyAsync(x => x.Slug == slug))
            slug = $"{slugBase}-{suffix++}";

        var recoveryCode = GenerateRecoveryCode();

        var tenant = new Tenant
        {
            Name = businessName,
            Slug = slug,
            BusinessProfile = new BusinessProfile
            {
                BusinessName = businessName,
                InvoicePrefix = "PF",
                TemplateKey = "modern"
            }
        };

        var user = new AppUser
        {
            Tenant = tenant,
            Email = email
        };

        user.PasswordHash = passwordHasher.HashPassword(user, password);
        user.RecoveryCodeHash = passwordHasher.HashPassword(user, recoveryCode);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        await SignInAsync(user);
        return (true, null, user, recoveryCode);
    }

    public async Task<(bool Success, string? Error, string? RecoveryCode)> LoginAsync(
        string email,
        string password)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email);

        if (user is null)
            return (false, "Invalid email or password.", null);

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (result == PasswordVerificationResult.Failed)
            return (false, "Invalid email or password.", null);

        string? recoveryCode = null;
        if (string.IsNullOrWhiteSpace(user.RecoveryCodeHash))
        {
            recoveryCode = GenerateRecoveryCode();
            user.RecoveryCodeHash = passwordHasher.HashPassword(user, recoveryCode);
            await db.SaveChangesAsync();
        }

        await SignInAsync(user);
        return (true, null, recoveryCode);
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(
        string currentPassword,
        string newPassword)
    {
        if (newPassword is null || newPassword.Length < 8)
            return (false, "New password must contain at least 8 characters.");

        if (string.IsNullOrWhiteSpace(currentPassword))
            return (false, "Enter your current password.");

        var userId = CurrentUserId();
        if (userId is null)
            return (false, "You must be logged in.");

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId.Value);
        if (user is null)
            return (false, "Account not found.");

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword);
        if (result == PasswordVerificationResult.Failed)
            return (false, "Current password is incorrect.");

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);

        // Rotating the recovery code is safest after a password change.
        var recoveryCode = GenerateRecoveryCode();
        user.RecoveryCodeHash = passwordHasher.HashPassword(user, recoveryCode);

        await db.SaveChangesAsync();
        return (true, recoveryCode);
    }

    public async Task<(bool Success, string? Error, string? RecoveryCode)> ResetPasswordWithRecoveryCodeAsync(
        string email,
        string recoveryCode,
        string newPassword)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        recoveryCode = (recoveryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (newPassword is null || newPassword.Length < 8)
            return (false, "New password must contain at least 8 characters.", null);

        if (string.IsNullOrWhiteSpace(recoveryCode))
            return (false, "Recovery code is required.", null);

        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email);
        if (user is null || string.IsNullOrWhiteSpace(user.RecoveryCodeHash))
            return (false, "Email or recovery code is incorrect.", null);

        var result = passwordHasher.VerifyHashedPassword(user, user.RecoveryCodeHash, recoveryCode);
        if (result == PasswordVerificationResult.Failed)
            return (false, "Email or recovery code is incorrect.", null);

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);

        // A reset consumes the old recovery code and creates a fresh one.
        var nextRecoveryCode = GenerateRecoveryCode();
        user.RecoveryCodeHash = passwordHasher.HashPassword(user, nextRecoveryCode);

        await db.SaveChangesAsync();

        await http.HttpContext!.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return (true, null, nextRecoveryCode);
    }

    public async Task<(bool Success, string? Error, string? RecoveryCode)> RotateRecoveryCodeAsync()
    {
        var userId = CurrentUserId();
        if (userId is null)
            return (false, "You must be logged in.", null);

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId.Value);
        if (user is null)
            return (false, "Account not found.", null);

        var recoveryCode = GenerateRecoveryCode();
        user.RecoveryCodeHash = passwordHasher.HashPassword(user, recoveryCode);
        await db.SaveChangesAsync();

        return (true, null, recoveryCode);
    }

    public async Task SignOutAsync()
    {
        await http.HttpContext!.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public Guid? CurrentTenantId()
    {
        var claim = http.HttpContext?.User.FindFirstValue("tenant_id");
        return Guid.TryParse(claim, out var tenantId) ? tenantId : null;
    }

    public string? CurrentEmail() => http.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    private Guid? CurrentUserId()
    {
        var claim = http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    private async Task SignInAsync(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("tenant_id", user.TenantId.ToString())
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await http.HttpContext!.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
                AllowRefresh = true
            });
    }

    private static string GenerateRecoveryCode()
    {
        Span<char> buffer = stackalloc char[12];

        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];

        return new string(buffer);
    }

    private static string MakeSlug(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-");

        return string.IsNullOrWhiteSpace(slug) ? "business" : slug;
    }
}
