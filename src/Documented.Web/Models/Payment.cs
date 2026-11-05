namespace Documented.Web.Models;

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }

    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public string Note { get; set; } = string.Empty;
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
}
