using Documented.Web.Data;
using Documented.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Documented")
        ?? "Data Source=App_Data/documented.db"));

builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<DocumentService>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    var tenants = scope.ServiceProvider.GetRequiredService<TenantService>();
    await tenants.EnsureDefaultTenantAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "Documented" }));

app.MapGet("/api/business", async (TenantService tenants) =>
    Results.Ok(await tenants.GetDefaultBusinessAsync()));

app.MapPut("/api/business", async (BusinessUpdateRequest request, TenantService tenants) =>
    Results.Ok(await tenants.UpdateDefaultBusinessAsync(request)));

app.MapGet("/api/documents", async (DocumentService documents, int limit = 25) =>
    Results.Ok(await documents.GetRecentAsync(Math.Clamp(limit, 1, 100))));

app.MapGet("/api/documents/{id:guid}", async (Guid id, DocumentService documents) =>
{
    var document = await documents.GetAsync(id);
    return document is null ? Results.NotFound() : Results.Ok(document);
});

app.MapGet("/api/public/{token}", async (string token, DocumentService documents) =>
{
    var document = await documents.GetByTokenAsync(token);
    return document is null ? Results.NotFound() : Results.Ok(document);
});

app.MapPost("/api/documents", async (CreateDocumentRequest request, DocumentService documents) =>
{
    try
    {
        var created = await documents.CreateAsync(request);
        return Results.Created($"/api/documents/{created.Id}", created);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapRazorPages();

app.Run();

public sealed record BusinessUpdateRequest(
    string BusinessName,
    string Address,
    string Phone,
    string Email,
    string LogoUrl,
    string Slogan,
    string BankName,
    string BankAccountNumber,
    string BankAccountName,
    string MobileMoneyName,
    string MobileMoneyNumber,
    string InvoicePrefix,
    string FooterText);

public sealed record CreateDocumentRequest(
    string DocumentType,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    string CustomerAddress,
    string Notes,
    decimal Discount,
    List<CreateDocumentItemRequest> Items);

public sealed record CreateDocumentItemRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice);
