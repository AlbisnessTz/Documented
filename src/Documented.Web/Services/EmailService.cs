using System.Net;
using System.Net.Mail;

namespace Documented.Web.Services;

/// <summary>
/// Sends email through any SMTP provider (Brevo, Resend SMTP, Gmail app password, ...).
/// Configure with SMTP_HOST, SMTP_PORT (default 587), SMTP_USER, SMTP_PASSWORD, SMTP_FROM.
/// When SMTP_HOST is not set, the message is written to the application log instead, so the
/// developer can still relay a reset link while email is not configured.
/// </summary>
public sealed class EmailService(IConfiguration config, ILogger<EmailService> logger)
{
    public async Task<bool> SendAsync(string to, string subject, string body)
    {
        var host = config["SMTP_HOST"];
        if (string.IsNullOrWhiteSpace(host))
        {
            logger.LogWarning("SMTP is not configured. Email to {To} was not sent. Subject: {Subject}. Body: {Body}",
                to, subject, body);
            return false;
        }

        try
        {
            var port = int.TryParse(config["SMTP_PORT"], out var p) ? p : 587;
            var user = config["SMTP_USER"];
            var from = config["SMTP_FROM"];
            if (string.IsNullOrWhiteSpace(from))
                from = user;

            if (string.IsNullOrWhiteSpace(from))
            {
                logger.LogError("SMTP_FROM (or SMTP_USER) must be set to send email.");
                return false;
            }

            using var client = new SmtpClient(host, port) { EnableSsl = true };
            if (!string.IsNullOrWhiteSpace(user))
                client.Credentials = new NetworkCredential(user, config["SMTP_PASSWORD"]);

            using var message = new MailMessage(from, to, subject, body);
            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {To}.", to);
            return false;
        }
    }
}
