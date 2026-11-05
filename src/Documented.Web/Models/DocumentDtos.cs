namespace Documented.Web.Models;

public sealed record BusinessProfileDto(
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

public sealed record DocumentListDto(
    Guid Id,
    string DocumentType,
    string Number,
    string PublicToken,
    string CustomerName,
    decimal Total,
    DateTime CreatedAtUtc,
    decimal AmountPaid);

public sealed record DocumentItemDto(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record DocumentCreatedDto(
    Guid Id,
    string DocumentType,
    string Number,
    string PublicToken,
    string CustomerName,
    decimal Total,
    DateTime CreatedAtUtc);

public sealed record DocumentDetailsDto(
    Guid Id,
    string DocumentType,
    string Number,
    string PublicToken,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    string CustomerAddress,
    string Notes,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    DateTime CreatedAtUtc,
    BusinessProfileDto Business,
    List<DocumentItemDto> Items,
    decimal AmountPaid);

public sealed record PaymentResultDto(decimal AmountPaid, decimal Balance);

public sealed record DebtorDto(
    string CustomerName,
    string CustomerPhone,
    decimal Outstanding,
    int InvoiceCount);

public sealed record ReportSummaryDto(
    decimal SalesThisMonth,
    decimal CollectedThisMonth,
    decimal OutstandingTotal,
    int DocumentsThisMonth,
    List<DebtorDto> Debtors);
