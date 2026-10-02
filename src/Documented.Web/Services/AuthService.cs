using System.Security.Claims;
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

    public async Task<(bool Success, string? Error, AppUser? User)> RegisterAsync(
        string businessName,
        string email,
        string password)
    {
        businessName = (businessName ?? string.Empty).Trim();
        email = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (businessName.Length < 2)
            return (false, "Business name is required.", null);

        if (email.Length < 5 || !email.Contains('@'))
            return (false, "Enter a valid email address.", null);

        if (password is null || password.Length < 8)
            return (false, "Password must contain at least 8 characters.", null);

        if (await db.Users.AnyAsync(x => x.Email == email))
            return (false, "An account with this email already exists.", null);

        var slugBase = MakeSlug(businessName);
        var slug = slugBase;
        var suffix = 2;
        while (await db.Tenants.AnyAsync(x => x.Slug == slug))
            slug = $"{slugBase}-{suffix++}";

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

        db.Users.Add(user);
        await db.SaveChangesAsync();

        await SignInAsync(user);
        return (true, null, user);
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email);

        if (user is null)
            return false;

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (result == PasswordVerificationResult.Failed)
            return false;

        await SignInAsync(user);
        return true;
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
