using Documented.Web.Data;
using Documented.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Services;

public sealed class TenantService(AppDbContext db)
{
    private static readonly Guid DefaultTenantId = Guid.Parse("8ea46da1-0b11-4afd-b726-76ba0c7dd001");

    public async Task<Tenant> EnsureDefaultTenantAsync()
    {
        var tenant = await db.Tenants
            .Include(x => x.BusinessProfile)
            .FirstOrDefaultAsync(x => x.Id == DefaultTenantId);

        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = DefaultTenantId,
                Name = "My Business",
                Slug = "my-business",
                BusinessProfile = new BusinessProfile
                {
                    TenantId = DefaultTenantId,
                    BusinessName = "My Business"
                }
            };

            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }
        else if (tenant.BusinessProfile is null)
        {
            tenant.BusinessProfile = new BusinessProfile
            {
                TenantId = tenant.Id,
                BusinessName = tenant.Name
            };
            await db.SaveChangesAsync();
        }

        return tenant;
    }

    public async Task<Tenant?> GetDefaultTenantAsync()
    {
        return await db.Tenants
            .Include(x => x.BusinessProfile)
            .FirstOrDefaultAsync(x => x.Id == DefaultTenantId);
    }

    public async Task<object> UpdateDefaultBusinessAsync(BusinessUpdateRequest request)
    {
        var tenant = await EnsureDefaultTenantAsync();
        var profile = tenant.BusinessProfile!;

        profile.BusinessName = Clean(request.BusinessName, "My Business");
        profile.Address = Clean(request.Address);
        profile.Phone = Clean(request.Phone);
        profile.Email = Clean(request.Email);
        profile.LogoUrl = Clean(request.LogoUrl);
        profile.Slogan = Clean(request.Slogan);
        profile.BankName = Clean(request.BankName);
        profile.BankAccountNumber = Clean(request.BankAccountNumber);
        profile.BankAccountName = Clean(request.BankAccountName);
        profile.MobileMoneyName = Clean(request.MobileMoneyName);
        profile.MobileMoneyNumber = Clean(request.MobileMoneyNumber);
        profile.InvoicePrefix = Clean(request.InvoicePrefix, "PF").ToUpperInvariant();
        profile.FooterText = Clean(request.FooterText);

        tenant.Name = profile.BusinessName;
        await db.SaveChangesAsync();

        return profile;
    }

    public static Guid DefaultId => DefaultTenantId;

    private static string Clean(string? value, string fallback = "")
        => (value ?? string.Empty).Trim() switch
        {
            { Length: > 0 } text => text,
            _ => fallback
        };
}
