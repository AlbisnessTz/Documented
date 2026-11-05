using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Data;

/// <summary>
/// EnsureCreated() only builds tables on an empty database, so tables added after the first
/// deployment must be created explicitly. Every statement here is idempotent.
/// </summary>
public static class SchemaUpgrader
{
    public static void Apply(AppDbContext db, bool isPostgres)
    {
        foreach (var sql in isPostgres ? Postgres : Sqlite)
            db.Database.ExecuteSqlRaw(sql);
    }

    private static readonly string[] Postgres =
    [
        """
        CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE CASCADE,
            "TokenHash" text NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "ExpiresAtUtc" timestamp with time zone NOT NULL,
            "UsedAtUtc" timestamp with time zone NULL
        )
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PasswordResetTokens_TokenHash"
            ON "PasswordResetTokens" ("TokenHash")
        """,
        """
        CREATE TABLE IF NOT EXISTS "Payments" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "DocumentId" uuid NOT NULL REFERENCES "Documents" ("Id") ON DELETE CASCADE,
            "Amount" numeric(18,2) NOT NULL,
            "Method" text NOT NULL,
            "Note" text NOT NULL,
            "PaidAtUtc" timestamp with time zone NOT NULL
        )
        """,
        """CREATE INDEX IF NOT EXISTS "IX_Payments_DocumentId" ON "Payments" ("DocumentId")""",
        """CREATE INDEX IF NOT EXISTS "IX_Payments_TenantId" ON "Payments" ("TenantId")"""
    ];

    private static readonly string[] Sqlite =
    [
        """
        CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_PasswordResetTokens" PRIMARY KEY,
            "UserId" TEXT NOT NULL,
            "TokenHash" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "ExpiresAtUtc" TEXT NOT NULL,
            "UsedAtUtc" TEXT NULL,
            CONSTRAINT "FK_PasswordResetTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
        )
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PasswordResetTokens_TokenHash"
            ON "PasswordResetTokens" ("TokenHash")
        """,
        """
        CREATE TABLE IF NOT EXISTS "Payments" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Payments" PRIMARY KEY,
            "TenantId" TEXT NOT NULL,
            "DocumentId" TEXT NOT NULL,
            "Amount" TEXT NOT NULL,
            "Method" TEXT NOT NULL,
            "Note" TEXT NOT NULL,
            "PaidAtUtc" TEXT NOT NULL,
            CONSTRAINT "FK_Payments_Documents_DocumentId" FOREIGN KEY ("DocumentId") REFERENCES "Documents" ("Id") ON DELETE CASCADE
        )
        """,
        """CREATE INDEX IF NOT EXISTS "IX_Payments_DocumentId" ON "Payments" ("DocumentId")""",
        """CREATE INDEX IF NOT EXISTS "IX_Payments_TenantId" ON "Payments" ("TenantId")"""
    ];
}
