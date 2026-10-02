namespace Documented.Web.Models;

public sealed class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string DocumentType { get; set; } = "Proforma";
    public string Number { get; set; } = string.Empty;
    public string PublicToken { get; set; } = Guid.NewGuid().ToString("N");

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public List<DocumentItem> Items { get; set; } = [];
}
