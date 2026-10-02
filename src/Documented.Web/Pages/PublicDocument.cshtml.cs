using Documented.Web.Data;
using Documented.Web.Models;
using Documented.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Documented.Web.Pages;

public sealed class PublicDocumentModel(DocumentService documents) : PageModel
{
    public DocumentDetailsDto? Document { get; private set; }

    public async Task<IActionResult> OnGetAsync(string token)
    {
        Document = await documents.GetByTokenAsync(token);
        return Page();
    }
}
