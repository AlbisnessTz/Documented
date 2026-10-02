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
    string FooterText);

public sealed record DocumentListDto(
    Guid Id,
    string DocumentType,
    string Number,
    string PublicToken,
    string CustomerName,
    decimal Total,
    DateTime CreatedAtUtc);

public sealed record DocumentItemDto(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

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
    List<DocumentItemDto> Items);
