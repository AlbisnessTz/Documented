using Documented.Web.Data;
using Documented.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Services;

public sealed class TenantService(AppDbContext db, AuthService auth)
{
    public async Task<Tenant?> GetCurrentTenantAsync()
    {
        var tenantId = auth.CurrentTenantId();
        if (tenantId is null)
            return null;

        return await db.Tenants
            .Include(x => x.BusinessProfile)
            .FirstOrDefaultAsync(x => x.Id == tenantId.Value);
    }

    public async Task<BusinessProfileDto> GetCurrentBusinessAsync()
    {
        var tenant = await GetCurrentTenantAsync()
            ?? throw new InvalidOperationException("Authenticated business workspace was not found.");

        return ToDto(tenant.BusinessProfile
            ?? throw new InvalidOperationException("Business profile was not found."));
    }

    public async Task<BusinessProfileDto> UpdateCurrentBusinessAsync(BusinessUpdateRequest request)
    {
        var tenant = await GetCurrentTenantAsync()
            ?? throw new InvalidOperationException("Authenticated business workspace was not found.");

        var profile = tenant.BusinessProfile ??= new BusinessProfile { TenantId = tenant.Id };

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
        profile.TemplateKey = request.TemplateKey is "worldlight" ? "worldlight" : "modern";

        tenant.Name = profile.BusinessName;
        await db.SaveChangesAsync();

        return ToDto(profile);
    }

    private static BusinessProfileDto ToDto(BusinessProfile profile) =>
        new(
            profile.BusinessName,
            profile.Address,
            profile.Phone,
            profile.Email,
            profile.LogoUrl,
            profile.Slogan,
            profile.BankName,
            profile.BankAccountNumber,
            profile.BankAccountName,
            profile.MobileMoneyName,
            profile.MobileMoneyNumber,
            profile.InvoicePrefix,
            profile.FooterText,
            profile.TemplateKey);

    private static string Clean(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
