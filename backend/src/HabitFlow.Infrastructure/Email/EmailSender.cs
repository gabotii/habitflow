using System.Net;
using System.Net.Mime;
using System.Text;
using System.Net.Mail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HabitFlow.Infrastructure.Email;

public enum EmailMode
{
    /// <summary>No e-mail: password reset and e-mail reminders are reported as unavailable.</summary>
    Disabled,

    /// <summary>Real delivery through an SMTP server.</summary>
    Smtp,

    /// <summary>Writes .eml files to a local folder instead of sending (development and tests).</summary>
    Pickup,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailMode Mode { get; set; } = EmailMode.Disabled;
    public string From { get; set; } = "HabitFlow <no-reply@habitflow.local>";

    /// <summary>Public URL of the web app, used to build links in e-mails (password reset).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpEnableSsl { get; set; } = true;
    public string PickupDirectory { get; set; } = "mail-pickup";
}

public interface IEmailSender
{
    bool IsEnabled { get; }

    Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken);
}

internal sealed class EmailSender(IOptions<EmailOptions> options, IHostEnvironment environment, ILogger<EmailSender> logger)
    : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsEnabled => _options.Mode switch
    {
        EmailMode.Smtp => !string.IsNullOrWhiteSpace(_options.SmtpHost),
        EmailMode.Pickup => true,
        _ => false,
    };

    public async Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("E-mail delivery is not configured.");
        }

        using var message = new MailMessage(new MailAddress(_options.From), new MailAddress(to))
        {
            Subject = subject,
            Body = textBody,
            IsBodyHtml = false,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            BodyTransferEncoding = TransferEncoding.QuotedPrintable,
        };

        using var client = CreateClient();
        await client.SendMailAsync(message, cancellationToken);

        // The recipient is personal data: log only that a message was sent.
        logger.LogInformation("E-mail sent via {Mode}", _options.Mode);
    }

    private SmtpClient CreateClient()
    {
        if (_options.Mode == EmailMode.Pickup)
        {
            // Relative paths are anchored to the API's content root, not to whatever directory the process started in.
            var directory = Path.GetFullPath(_options.PickupDirectory, environment.ContentRootPath);
            Directory.CreateDirectory(directory);
            return new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = directory,
            };
        }

        var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort) { EnableSsl = _options.SmtpEnableSsl };
        if (!string.IsNullOrEmpty(_options.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);
        }

        return client;
    }
}
