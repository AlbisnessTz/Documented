# Documented

Documented is a reusable online/offline business document platform. It is designed for many independent businesses to create, brand, print and share professional documents.

## Current MVP

- Business registration and login.
- Tenant-isolated business workspaces.
- SQLite local/offline database.
- PostgreSQL provider for cloud deployment.
- Configurable business profile and payment details.
- Proforma creation with automatic numbering and totals.
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
