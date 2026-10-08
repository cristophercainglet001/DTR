using System.Net;
using System.Net.Mail;

namespace DepEdDTRSystem.Services;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var host = configuration["Email:SmtpHost"];
        var fromAddress = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException(
                "Configure Email:SmtpHost and Email:FromAddress before sending email.");
        }

        var port = configuration.GetValue<int?>("Email:Port") ?? 587;
        var message = new MailMessage(fromAddress, recipient, subject, body)
        {
            From = new MailAddress(fromAddress, configuration["Email:FromName"] ?? "DepEd DTR"),
        };

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = configuration.GetValue("Email:UseSsl", true),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000,
        };

        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(username, password);
        }

        using (message)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, cancellationToken);
        }
    }
}
