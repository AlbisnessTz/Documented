using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;

namespace Documented.Web.Services;

/// <summary>
/// Sends email. Two providers are supported, checked in this order:
///
/// 1. Brevo HTTPS API (recommended, and the only option that works on Render's free plan,
///    which blocks outbound SMTP ports 25, 465 and 587). Set BREVO_API_KEY and MAIL_FROM_EMAIL
///    (a sender address verified in Brevo). MAIL_FROM_NAME is optional.
/// 2. Any SMTP server, for hosts that allow it. Set SMTP_HOST, SMTP_PORT (default 587),
///    SMTP_USER, SMTP_PASSWORD and SMTP_FROM.
///
/// When neither is configured the message is written to the application log instead, so the
/// developer can still relay a reset link by hand.
/// </summary>
public sealed class EmailService(HttpClient http, IConfiguration config, ILogger<EmailService> logger)
{
    public async Task<bool> SendAsync(string to, string subject, string body)
    {
        if (!string.IsNullOrWhiteSpace(config["BREVO_API_KEY"]))
            return await SendWithBrevoAsync(to, subject, body);

        if (!string.IsNullOrWhiteSpace(config["SMTP_HOST"]))
            return await SendWithSmtpAsync(to, subject, body);

        logger.LogWarning("No email provider is configured. Email to {To} was not sent. Subject: {Subject}. Body: {Body}",
            to, subject, body);
        return false;
    }

    private async Task<bool> SendWithBrevoAsync(string to, string subject, string body)
    {
        var fromEmail = config["MAIL_FROM_EMAIL"];
        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            logger.LogError("MAIL_FROM_EMAIL must be set to a sender address verified in Brevo.");
            return false;
        }

        var fromName = config["MAIL_FROM_NAME"];
        if (string.IsNullOrWhiteSpace(fromName))
            fromName = "Documented";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
            {
                Content = JsonContent.Create(new
                {
                    sender = new { name = fromName, email = fromEmail },
                    to = new[] { new { email = to } },
                    subject,
                    textContent = body
                })
            };
            request.Headers.Add("api-key", config["BREVO_API_KEY"]);

            using var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return true;

            var detail = await response.Content.ReadAsStringAsync();
            logger.LogError("Brevo rejected the email to {To}: {Status} {Detail}", to, (int)response.StatusCode, detail);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {To} through Brevo.", to);
            return false;
        }
    }

    private async Task<bool> SendWithSmtpAsync(string to, string subject, string body)
    {
        try
        {
            var host = config["SMTP_HOST"]!;
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
