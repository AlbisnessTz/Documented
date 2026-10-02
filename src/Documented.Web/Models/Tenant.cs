namespace Documented.Web.Models;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public BusinessProfile? BusinessProfile { get; set; }
    public List<Document> Documents { get; set; } = [];
}
