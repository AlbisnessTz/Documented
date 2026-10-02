using Documented.Web.Data;
using Documented.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Services;

public sealed class DocumentService(AppDbContext db, TenantService tenants)
{
    public async Task<List<DocumentListDto>> GetRecentAsync(int limit)
    {
        var tenantId = TenantService.DefaultId;

        return await db.Documents
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new DocumentListDto(
                x.Id,
                x.DocumentType,
                x.Number,
                x.PublicToken,
                x.CustomerName,
                x.Total,
                x.CreatedAtUtc))
            .ToListAsync();
    }

    public async Task<DocumentDetailsDto?> GetAsync(Guid id)
    {
        var document = await db.Documents
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Tenant)
            .ThenInclude(x => x.BusinessProfile)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantService.DefaultId);

        return document is null ? null : ToDetails(document);
    }

    public async Task<DocumentDetailsDto?> GetByTokenAsync(string token)
    {
        var document = await db.Documents
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Tenant)
            .ThenInclude(x => x.BusinessProfile)
            .FirstOrDefaultAsync(x => x.PublicToken == token);

        return document is null ? null : ToDetails(document);
    }

    public async Task<DocumentCreatedDto> CreateAsync(CreateDocumentRequest request)
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
        var prefix = string.IsNullOrWhiteSpace(tenant.BusinessProfile?.InvoicePrefix)
            ? "PF"
            : tenant.BusinessProfile.InvoicePrefix;

        var usedNumbers = await db.Documents
            .Where(x => x.TenantId == tenant.Id && x.Number.StartsWith(prefix + "-"))
            .Select(x => x.Number)
            .ToListAsync();

        var next = usedNumbers
            .Select(x => x[(prefix.Length + 1)..])
            .Select(x => int.TryParse(x, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var items = cleanItems.Select(x => new DocumentItem
        {
            Description = x.Description,
            Quantity = x.Quantity,
            UnitPrice = decimal.Round(x.UnitPrice, 2),
            LineTotal = decimal.Round(x.Quantity * x.UnitPrice, 2)
        }).ToList();

        var subtotal = items.Sum(x => x.LineTotal);
        var discount = Math.Clamp(request.Discount, 0, subtotal);

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
            Total = subtotal - discount,
            Items = items
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();

        return new DocumentCreatedDto(
            document.Id,
            document.DocumentType,
            document.Number,
            document.PublicToken,
            document.CustomerName,
            document.Total,
            document.CreatedAtUtc);
    }

    private static DocumentDetailsDto ToDetails(Document document)
    {
        var business = document.Tenant.BusinessProfile!;
        return new DocumentDetailsDto(
            document.Id,
            document.DocumentType,
            document.Number,
            document.PublicToken,
            document.CustomerName,
            document.CustomerPhone,
            document.CustomerEmail,
            document.CustomerAddress,
            document.Notes,
            document.Subtotal,
            document.Discount,
            document.Total,
            document.CreatedAtUtc,
            new BusinessProfileDto(
                business.BusinessName,
                business.Address,
                business.Phone,
                business.Email,
                business.LogoUrl,
                business.Slogan,
                business.BankName,
                business.BankAccountNumber,
                business.BankAccountName,
                business.MobileMoneyName,
                business.MobileMoneyNumber,
                business.InvoicePrefix,
                business.FooterText),
            document.Items
                .OrderBy(x => x.Id)
                .Select(x => new DocumentItemDto(x.Description, x.Quantity, x.UnitPrice, x.LineTotal))
                .ToList());
    }
}
