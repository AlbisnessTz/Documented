using Documented.Web.Data;
using Documented.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account";
        options.Cookie.Name = "Documented.Auth";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Documented")
        ?? "Data Source=App_Data/documented.db"));

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<DocumentService>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();

app.Use(async (context, next) =>
{
    var requiresAccount = context.Request.Path == "/" ||
                          context.Request.Path.StartsWithSegments("/Setup");

    if (requiresAccount && !(context.User.Identity?.IsAuthenticated ?? false))
    {
        context.Response.Redirect("/Account");
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "Documented" }));

app.MapGet("/api/session", (AuthService auth) =>
    Results.Ok(new
    {
        authenticated = auth.CurrentTenantId() is not null,
        email = auth.CurrentEmail()
    }));

app.MapPost("/api/auth/register", async (RegisterRequest request, AuthService auth) =>
{
    var result = await auth.RegisterAsync(request.BusinessName, request.Email, request.Password);
    return result.Success
        ? Results.Ok(new { message = "Account created." })
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/auth/login", async (LoginRequest request, AuthService auth) =>
    await auth.LoginAsync(request.Email, request.Password)
        ? Results.Ok(new { message = "Logged in." })
        : Results.BadRequest(new { error = "Invalid email or password." }));

app.MapPost("/api/auth/logout", async (AuthService auth) =>
{
    await auth.SignOutAsync();
    return Results.Ok(new { message = "Logged out." });
});

app.MapGet("/api/business", async (TenantService tenants) =>
    Results.Ok(await tenants.GetCurrentBusinessAsync()))
    .RequireAuthorization();

app.MapPut("/api/business", async (BusinessUpdateRequest request, TenantService tenants) =>
    Results.Ok(await tenants.UpdateCurrentBusinessAsync(request)))
    .RequireAuthorization();

app.MapGet("/api/documents", async (DocumentService documents, int limit = 25) =>
    Results.Ok(await documents.GetRecentAsync(Math.Clamp(limit, 1, 100))))
    .RequireAuthorization();

app.MapGet("/api/documents/{id:guid}", async (Guid id, DocumentService documents) =>
{
    var document = await documents.GetAsync(id);
    return document is null ? Results.NotFound() : Results.Ok(document);
}).RequireAuthorization();

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
}).RequireAuthorization();

app.MapRazorPages();

app.Run();

public sealed record RegisterRequest(string BusinessName, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);

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
    string FooterText,
    string TemplateKey);

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
