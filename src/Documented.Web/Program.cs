using System.Data.Common;
using Documented.Web.Data;
using Documented.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<DocumentService>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    await EnsureRecoveryCodeColumnAsync(db, provider);
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
        ? Results.Ok(new { message = "Account created.", recoveryCode = result.RecoveryCode })
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/auth/login", async (LoginRequest request, AuthService auth) =>
{
    var result = await auth.LoginAsync(request.Email, request.Password);
    return result.Success
        ? Results.Ok(new { message = "Logged in.", recoveryCode = result.RecoveryCode })
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/auth/change-password", async (ChangePasswordRequest request, AuthService auth) =>
{
    var result = await auth.ChangePasswordAsync(request.CurrentPassword, request.NewPassword);
    return result.Success
        ? Results.Ok(new { message = "Password changed.", recoveryCode = result.RecoveryCode })
        : Results.BadRequest(new { error = result.Error });
}).RequireAuthorization();

app.MapPost("/api/auth/reset-password", async (ResetPasswordRequest request, AuthService auth) =>
{
    var result = await auth.ResetPasswordWithRecoveryCodeAsync(
        request.Email,
        request.RecoveryCode,
        request.NewPassword);

    return result.Success
        ? Results.Ok(new { message = "Password reset.", recoveryCode = result.RecoveryCode })
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/api/auth/recovery-code", async (AuthService auth) =>
{
    var result = await auth.RotateRecoveryCodeAsync();
    return result.Success
        ? Results.Ok(new { message = "Recovery code generated.", recoveryCode = result.RecoveryCode })
        : Results.BadRequest(new { error = result.Error });
}).RequireAuthorization();

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

static async Task EnsureRecoveryCodeColumnAsync(AppDbContext db, string provider)
{
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        if (provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            await AddPostgresColumnIfMissingAsync(connection, "RecoveryCodeHash");
        }
        else
        {
            var exists = await SqliteColumnExistsAsync(connection, "Users", "RecoveryCodeHash");
            if (!exists)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE "Users" ADD COLUMN "RecoveryCodeHash" TEXT NULL;";
                await command.ExecuteNonQueryAsync();
            }
        }
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task AddPostgresColumnIfMissingAsync(DbConnection connection, string columnName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_name = 'Users'
                  AND column_name = 'RecoveryCodeHash'
            ) THEN
                ALTER TABLE "Users" ADD COLUMN "RecoveryCodeHash" text NULL;
            END IF;
        END $$;
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task<bool> SqliteColumnExistsAsync(DbConnection connection, string tableName, string columnName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info("{tableName}");";

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

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
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ResetPasswordRequest(string Email, string RecoveryCode, string NewPassword);

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
