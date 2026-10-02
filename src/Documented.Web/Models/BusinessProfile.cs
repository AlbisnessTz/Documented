namespace Documented.Web.Models;

public sealed class BusinessProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string BusinessName { get; set; } = "My Business";
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string Slogan { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;
    public string BankAccountNumber { get; set; } = string.Empty;
    public string BankAccountName { get; set; } = string.Empty;
    public string MobileMoneyName { get; set; } = string.Empty;
    public string MobileMoneyNumber { get; set; } = string.Empty;

    public string InvoicePrefix { get; set; } = "PF";
    public string FooterText { get; set; } = "With our company, you are in good hands.";
    public string TemplateKey { get; set; } = "modern";

    public Tenant Tenant { get; set; } = null!;
}