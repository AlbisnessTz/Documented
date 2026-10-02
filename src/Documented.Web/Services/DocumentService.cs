using Documented.Web.Data;
using Documented.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Services;

public sealed class DocumentService(AppDbContext db, TenantService tenants)
{
    public async Task<List<object>> GetRecentAsync(int limit)
    {
        var tenantId = TenantService.DefaultId;

        return await db.Documents
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.DocumentType,
                x.Number,
                x.PublicToken,
                x.CustomerName,
                x.Total,
                x.CreatedAtUtc
            })
            .Cast<object>()
            .ToListAsync();
    }

    public async Task<object?> GetAsync(Guid id)
    {
        return await db.Documents
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Tenant)
                .ThenInclude(x => x.BusinessProfile)
            .Where(x => x.Id == id && x.TenantId == TenantService.DefaultId)
            .Select(x => new
            {
                x.Id,
                x.DocumentType,
                x.Number,
                x.PublicToken,
                x.CustomerName,
                x.CustomerPhone,
                x.CustomerEmail,
                x.CustomerAddress,
                x.Notes,
                x.Subtotal,
                x.Discount,
                x.Total,
                x.CreatedAtUtc,
                Business = x.Tenant.BusinessProfile,
                Items = x.Items.OrderBy(i => i.Description)
            })
            .FirstOrDefaultAsync();
    }

    public async Task<object> CreateAsync(CreateDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new ArgumentException("Customer name is required.");

        var cleanItems = (request.Items ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Description) && x.Quantity > 0 && x.UnitPrice >= 0)
            .Select(x => new
            {
                Description = x.Description.Trim(),
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice
            })
            .ToList();

        if (cleanItems.Count == 0)
            throw new ArgumentException("Add at least one document item.");

        var tenant = await tenants.EnsureDefaultTenantAsync();
        var prefix = tenant.BusinessProfile?.InvoicePrefix ?? "PF";

        var usedNumbers = await db.Documents
            .Where(x => x.TenantId == tenant.Id && x.Number.StartsWith(prefix + "-"))
            .Select(x => x.Number)
            .ToListAsync();

        var next = 1;
        var parsed = usedNumbers
            .Select(x => x[(prefix.Length + 1)..])
            .Select(x => int.TryParse(x, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        next = parsed + 1;

        var items = cleanItems.Select(x => new DocumentItem
        {
            Description = x.Description,
            Quantity = x.Quantity,
            UnitPrice = decimal.Round(x.UnitPrice, 2),
            LineTotal = decimal.Round(x.Quantity * x.UnitPrice, 2)
        }).ToList();

        var subtotal = items.Sum(x => x.LineTotal);
        var discount = Math.Clamp(request.Discount, 0, subtotal);
        var total = subtotal - discount;

        var document = new Document
        {
            TenantId = tenant.Id,
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? "Proforma" : request.DocumentType.Trim(),
            Number = $"{prefix}-{next:000000}",
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone?.Trim() ?? string.Empty,
            CustomerEmail = request.CustomerEmail?.Trim() ?? string.Empty,
            CustomerAddress = request.CustomerAddress?.Trim() ?? string.Empty,
            Notes = request.Notes?.Trim() ?? string.Empty,
            Subtotal = subtotal,
            Discount = discount,
            Total = total,
            Items = items
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();

        return new
        {
            document.Id,
            document.DocumentType,
            document.Number,
            document.PublicToken,
            document.CustomerName,
            document.Total,
            document.CreatedAtUtc
        };
    }
}
