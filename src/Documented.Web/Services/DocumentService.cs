using Documented.Web.Data;
using Documented.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Services;

public sealed class DocumentService(AppDbContext db, TenantService tenants)
{
    public static readonly string[] DocumentTypes = ["Proforma", "Invoice", "Receipt", "Quotation"];

    // Tanzania has no daylight saving time, so a fixed +3 hour offset is exact.
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(3);

    public async Task<List<DocumentListDto>> GetRecentAsync(int limit)
    {
        var tenant = await tenants.GetCurrentTenantAsync();
        if (tenant is null)
            return [];

        var rows = await db.Documents
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
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
            .ToListAsync();

        var paid = await PaidByDocumentAsync(rows.Select(x => x.Id).ToList());

        return rows
            .Select(x => new DocumentListDto(
                x.Id,
                x.DocumentType,
                x.Number,
                x.PublicToken,
                x.CustomerName,
                x.Total,
                x.CreatedAtUtc,
                paid.GetValueOrDefault(x.Id)))
            .ToList();
    }

    public async Task<DocumentDetailsDto?> GetAsync(Guid id)
    {
        var tenant = await tenants.GetCurrentTenantAsync();
        if (tenant is null)
            return null;

        var document = await db.Documents
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Tenant)
            .ThenInclude(x => x.BusinessProfile)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id);

        if (document is null)
            return null;

        var paid = await PaidByDocumentAsync([document.Id]);
        return ToDetails(document, paid.GetValueOrDefault(document.Id));
    }

    public async Task<DocumentDetailsDto?> GetByTokenAsync(string token)
    {
        var document = await db.Documents
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Tenant)
            .ThenInclude(x => x.BusinessProfile)
            .FirstOrDefaultAsync(x => x.PublicToken == token);

        if (document is null)
            return null;

        var paid = await PaidByDocumentAsync([document.Id]);
        return ToDetails(document, paid.GetValueOrDefault(document.Id));
    }

    public async Task<DocumentCreatedDto> CreateAsync(CreateDocumentRequest request)
    {
        var tenant = await tenants.GetCurrentTenantAsync()
            ?? throw new InvalidOperationException("Authenticated business workspace was not found.");

        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new ArgumentException("Customer name is required.");

        var type = NormalizeType(request.DocumentType);

        var cleanItems = (request.Items ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Description) && x.Quantity > 0 && x.UnitPrice >= 0)
            .Select(x => new { Description = x.Description.Trim(), x.Quantity, x.UnitPrice })
            .ToList();

        if (cleanItems.Count == 0)
            throw new ArgumentException("Add at least one document item.");

        var tenantPrefix = tenant.BusinessProfile?.InvoicePrefix;
        var prefix = type switch
        {
            "Invoice" => "INV",
            "Receipt" => "RCT",
            "Quotation" => "QT",
            _ => string.IsNullOrWhiteSpace(tenantPrefix) ? "PF" : tenantPrefix.Trim()
        };

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
            DocumentType = type,
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

        // A receipt confirms money already received, so it is recorded as paid in full.
        if (type == "Receipt" && document.Total > 0)
        {
            db.Payments.Add(new Payment
            {
                TenantId = tenant.Id,
                DocumentId = document.Id,
                Amount = document.Total,
                Method = CleanMethod(request.PaymentMethod),
                Note = "Paid in full when the receipt was issued."
            });
        }

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

    public async Task<PaymentResultDto?> RecordPaymentAsync(Guid documentId, RecordPaymentRequest request)
    {
        var tenant = await tenants.GetCurrentTenantAsync();
        if (tenant is null)
            return null;

        var document = await db.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.TenantId == tenant.Id);

        if (document is null)
            return null;

        if (document.DocumentType != "Invoice")
            throw new ArgumentException("Payments can only be recorded on invoices. Create an invoice first.");

        var amount = decimal.Round(request.Amount, 2);
        if (amount <= 0)
            throw new ArgumentException("Enter an amount greater than zero.");

        var paid = (await PaidByDocumentAsync([document.Id])).GetValueOrDefault(document.Id);
        var balance = document.Total - paid;

        if (amount > balance)
            throw new ArgumentException($"That is more than the remaining balance of {balance:N2}.");

        db.Payments.Add(new Payment
        {
            TenantId = tenant.Id,
            DocumentId = document.Id,
            Amount = amount,
            Method = CleanMethod(request.Method),
            Note = (request.Note ?? string.Empty).Trim()
        });

        await db.SaveChangesAsync();
        return new PaymentResultDto(paid + amount, balance - amount);
    }

    public async Task<ReportSummaryDto> GetSummaryAsync()
    {
        var tenant = await tenants.GetCurrentTenantAsync();
        if (tenant is null)
            return new ReportSummaryDto(0, 0, 0, 0, []);

        var documents = await db.Documents
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .Select(x => new
            {
                x.Id,
                x.DocumentType,
                x.CustomerName,
                x.CustomerPhone,
                x.Total,
                x.CreatedAtUtc
            })
            .ToListAsync();

        var payments = await db.Payments
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id)
            .Select(x => new { x.DocumentId, x.Amount, x.PaidAtUtc })
            .ToListAsync();

        var monthStartUtc = MonthStartUtc(DateTime.UtcNow);

        var paidByDocument = payments
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var salesThisMonth = documents
            .Where(x => (x.DocumentType == "Invoice" || x.DocumentType == "Receipt") && x.CreatedAtUtc >= monthStartUtc)
            .Sum(x => x.Total);

        var collectedThisMonth = payments
            .Where(x => x.PaidAtUtc >= monthStartUtc)
            .Sum(x => x.Amount);

        var documentsThisMonth = documents.Count(x => x.CreatedAtUtc >= monthStartUtc);

        var debts = documents
            .Where(x => x.DocumentType == "Invoice")
            .Select(x => new
            {
                x.CustomerName,
                x.CustomerPhone,
                Outstanding = x.Total - paidByDocument.GetValueOrDefault(x.Id)
            })
            .Where(x => x.Outstanding > 0)
            .ToList();

        var debtors = debts
            .GroupBy(x => new { Name = x.CustomerName.Trim().ToLowerInvariant(), x.CustomerPhone })
            .Select(g => new DebtorDto(
                g.First().CustomerName,
                g.Key.CustomerPhone,
                g.Sum(x => x.Outstanding),
                g.Count()))
            .OrderByDescending(x => x.Outstanding)
            .Take(10)
            .ToList();

        return new ReportSummaryDto(
            salesThisMonth,
            collectedThisMonth,
            debts.Sum(x => x.Outstanding),
            documentsThisMonth,
            debtors);
    }

    public static DateTime MonthStartUtc(DateTime utcNow)
    {
        var local = utcNow + LocalOffset;
        var startLocal = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return DateTime.SpecifyKind(startLocal - LocalOffset, DateTimeKind.Utc);
    }

    private async Task<Dictionary<Guid, decimal>> PaidByDocumentAsync(List<Guid> documentIds)
    {
        if (documentIds.Count == 0)
            return [];

        var rows = await db.Payments
            .AsNoTracking()
            .Where(x => documentIds.Contains(x.DocumentId))
            .Select(x => new { x.DocumentId, x.Amount })
            .ToListAsync();

        return rows
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
    }

    private static string NormalizeType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Proforma";

        var match = DocumentTypes.FirstOrDefault(x => x.Equals(raw.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException("Unknown document type.");
    }

    private static string CleanMethod(string? raw)
    {
        var method = (raw ?? string.Empty).Trim();
        if (method.Length == 0)
            return "Cash";

        return method.Length > 40 ? method[..40] : method;
    }

    private static DocumentDetailsDto ToDetails(Document document, decimal amountPaid)
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
                business.FooterText,
                business.TemplateKey),
            document.Items
                .OrderBy(x => x.Id)
                .Select(x => new DocumentItemDto(x.Description, x.Quantity, x.UnitPrice, x.LineTotal))
                .ToList(),
            amountPaid);
    }
}
