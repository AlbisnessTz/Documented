# Documented

Documented is a reusable online/offline business document platform. It is designed for many independent businesses to create, brand, print and share professional documents.

## Current MVP

- Business registration and login.
- Tenant-isolated business workspaces.
- SQLite local/offline database.
- PostgreSQL provider for cloud deployment.
- Configurable business profile and payment details.
- Proforma, quotation, invoice and receipt creation with automatic numbering (PF / QT / INV / RCT) and totals.
- Payments on invoices (part payments allowed, never above the balance), debtors list and a monthly sales summary.
- Forgot password / reset password by email, with 30-minute single-use links and rate limiting on login and reset.
- Public shareable links.
- WhatsApp and copy-link sharing.
- Browser print / Save PDF.
- PWA shell and offline document queue.
- Modern and World Light-inspired document templates.

## Local development

```bash
dotnet restore Documented.sln
dotnet run --project src/Documented.Web
```

The default local database is `App_Data/documented.db`.

## Cloud database

Set:

```text
DOCUMENTED_DB_PROVIDER=postgres
DOCUMENTED_CONNECTION=<your PostgreSQL connection string>
```

The application keeps the same tenant-aware model while switching the EF Core provider.

## Docker

```bash
docker build -t documented .
docker run -p 8080:8080 documented
```

For production, provide the PostgreSQL connection string through the hosting platform's secret/environment-variable system rather than committing credentials.

## Password reset email

Set these environment variables on the host (never commit them):

```text
DOCUMENTED_PUBLIC_URL=https://your-site.example.com
BREVO_API_KEY=<API key from Brevo>
MAIL_FROM_EMAIL=<sender address verified in Brevo>
MAIL_FROM_NAME=Documented        (optional)
```

- `DOCUMENTED_PUBLIC_URL` is required in production. Reset links are built from it, never from the request's Host header.
- Email goes out through Brevo's HTTPS API. Render's free plan blocks outbound SMTP (ports 25, 465, 587), so SMTP will not work there.
- On hosts that allow SMTP you can use `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM` instead.
- With no provider configured, the reset message is written to the application log so the developer can still pass the link on.

## Hosting notes

- Render's free PostgreSQL database expires 30 days after it is created and is deleted 14 days later. Check the expiry date in the Render dashboard, and move to a paid database or another Postgres host before it passes.
- The free web service sleeps after 15 minutes without traffic and takes about a minute to wake.
- New tables are added to existing databases by `SchemaUpgrader` at startup; it is safe to run repeatedly.
