# Documented

Documented is a reusable online/offline business document platform. The first milestone is a configurable proforma invoice that can be branded for different businesses and shared by a public link.

## Current foundation

- ASP.NET Core 10 web application using Razor Pages and Minimal APIs.
- SQLite persistence for local/offline-first operation.
- Tenant, business profile, document, and document-item data model.
- Configurable business name, logo URL, contacts, address, slogan, payment details, document prefix, and footer.
- Proforma document creation with automatic totals and numbering.
- Public shareable document URL.
- WhatsApp sharing, copy-link sharing, and browser print/save-to-PDF.
- PWA manifest and service worker shell.
- Offline queue for documents created while the device has no connection.

## Architecture direction

The database is tenant-aware from the beginning so the application can grow into a multi-business SaaS without rewriting the document model. Authentication, cloud PostgreSQL, reliable sync conflict handling, subscriptions, and richer document templates will be added in later milestones.

## Development

```bash
dotnet restore Documented.sln
dotnet run --project src/Documented.Web
```

The app creates its SQLite database under `App_Data/documented.db`.
