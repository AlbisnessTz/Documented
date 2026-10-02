using Documented.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Pages;

public sealed class PublicDocumentModel(AppDbContext db) : PageModel
{
    public dynamic? Document { get; private set; }

    public async Task<IActionResult> OnGetAsync(string token)
    {
        Document = await db.Documents
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Tenant)
                .ThenInclude(x => x.BusinessProfile)
            .Where(x => x.PublicToken == token)
            .Select(x => new
            {
                x.DocumentType,
                x.Number,
                x.CustomerName,
                x.CustomerPhone,
                x.CustomerAddress,
                x.Notes,
                x.Subtotal,
                x.Discount,
                x.Total,
                x.CreatedAtUtc,
                Business = x.Tenant.BusinessProfile!,
                Items = x.Items.OrderBy(i => i.Id).ToList()
            })
            .FirstOrDefaultAsync();

        return Page();
    }
}
