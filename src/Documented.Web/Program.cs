using Documented.Web.Data;
using Documented.Web.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Slows down password guessing and reset-email abuse. Behind Render's proxy the real
// client address arrives in X-Forwarded-For.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("auth", context =>
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var client = !string.IsNullOrWhiteSpace(forwarded)
            ? forwarded.Split(',')[0].Trim()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});
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

var provider = builder.Configuration["Database:Provider"]
    ?? Environment.GetEnvironmentVariable("DOCUMENTED_DB_PROVIDER")
    ?? "sqlite";

var connectionString = builder.Configuration.GetConnectionString("Documented")
    ?? Environment.GetEnvironmentVariable("DOCUMENTED_CONNECTION")
    ?? "Data Source=App_Data/documented.db";

if (provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
{
    connectionString = NormalizePostgresConnectionString(connectionString);
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(connectionString);
    else
        options.UseSqlite(connectionString);
});

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<DocumentService>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SchemaUpgrader.Apply(db, provider.Equals("postgres", StringComparison.OrdinalIgnoreCase));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
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

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    app = "Documented",
    database = provider
}));

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
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/login", async (LoginRequest request, AuthService auth) =>
    await auth.LoginAsync(request.Email, request.Password)
        ? Results.Ok(new { message = "Logged in." })
        : Results.BadRequest(new { error = "Invalid email or password." }))
    .RequireRateLimiting("auth");

app.MapPost("/api/auth/forgot", async (
    ForgotPasswordRequest request,
    AuthService auth,
    HttpContext context,
    IConfiguration config) =>
{
    var baseUrl = config["DOCUMENTED_PUBLIC_URL"];
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
        baseUrl = $"{scheme}://{context.Request.Host}";
    }

    await auth.ForgotPasswordAsync(request.Email, baseUrl);

    // Same answer whether or not the email has an account.
    return Results.Ok(new { message = "If an account exists for that email, a reset link has been sent." });
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/reset", async (ResetPasswordRequest request, AuthService auth) =>
{
    var result = await auth.ResetPasswordAsync(request.Token, request.Password);
    return result.Success
        ? Results.Ok(new { message = "Password updated. You can log in now." })
        : Results.BadRequest(new { error = result.Error });
}).RequireRateLimiting("auth");

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

app.MapPost("/api/documents/{id:guid}/payments", async (Guid id, RecordPaymentRequest request, DocumentService documents) =>
{
    try
    {
        var result = await documents.RecordPaymentAsync(id, request);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/reports/summary", async (DocumentService documents) =>
    Results.Ok(await documents.GetSummaryAsync()))
    .RequireAuthorization();

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

static string NormalizePostgresConnectionString(string raw)
{
    raw = raw.Trim();

    if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return raw;
    }

    var uri = new Uri(raw);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
    };

    var userInfo = uri.UserInfo.Split(':', 2);
    if (userInfo.Length > 0 && userInfo[0].Length > 0)
        builder.Username = Uri.UnescapeDataString(userInfo[0]);

    if (userInfo.Length > 1)
        builder.Password = Uri.UnescapeDataString(userInfo[1]);

    foreach (var parameter in uri.Query.TrimStart('?')
                 .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var parts = parameter.Split('=', 2);
        if (parts.Length != 2)
            continue;

        var key = Uri.UnescapeDataString(parts[0]);
        var value = Uri.UnescapeDataString(parts[1]);

        if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<SslMode>(value, true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }
    }

    return builder.ConnectionString;
}

public sealed record RegisterRequest(string BusinessName, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string Password);

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
    List<CreateDocumentItemRequest> Items,
    string? PaymentMethod = null);

public sealed record RecordPaymentRequest(decimal Amount, string? Method = null, string? Note = null);

public sealed record CreateDocumentItemRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice);
