using Exb.Core.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Exb.Core.Mail;

public sealed record OutgoingMail(
    string ToAddress,
    string? ToName,
    string Subject,
    string HtmlBody,
    string? TextBody,
    IReadOnlyList<string> AttachmentPaths);

/// <summary>
/// Actually puts a message on the wire.
///
/// Every message is written to the Outbox table first and sent from there, so
/// the transport is only ever the last hop. That ordering is what lets the whole
/// evening pipeline be rehearsed against real registration data with nothing
/// configured: messages queue, an admin reads exactly what would have gone out,
/// and not one visitor is emailed by accident.
/// </summary>
public interface IMailTransport
{
    string Name { get; }

    /// <summary>False when no server is configured. The queue then simply holds.</summary>
    bool CanSend { get; }

    Task SendAsync(OutgoingMail mail, CancellationToken ct = default);
}

/// <summary>The default transport: queue only, send nothing.</summary>
public sealed class HeldMailTransport : IMailTransport
{
    public string Name => "outbox";
    public bool CanSend => false;

    public Task SendAsync(OutgoingMail mail, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Mail provider is set to 'outbox': messages are queued for review and not sent. " +
            "Set Settings > Delivery > mail provider to 'smtp' and configure a server to send.");
}

public sealed class SmtpMailTransport(MailSettings settings) : IMailTransport
{
    public string Name => "smtp";

    public bool CanSend =>
        !string.IsNullOrWhiteSpace(settings.Host) && !string.IsNullOrWhiteSpace(settings.FromAddress);

    public async Task SendAsync(OutgoingMail mail, CancellationToken ct = default)
    {
        if (!CanSend)
            throw new InvalidOperationException("SMTP host or from-address is not configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        if (!string.IsNullOrWhiteSpace(settings.ReplyTo))
            message.ReplyTo.Add(MailboxAddress.Parse(settings.ReplyTo));

        // The rehearsal safety catch: when set, everything goes to one inbox.
        string to = string.IsNullOrWhiteSpace(settings.RedirectAllTo) ? mail.ToAddress : settings.RedirectAllTo;
        message.To.Add(new MailboxAddress(mail.ToName ?? to, to));

        message.Subject = string.IsNullOrWhiteSpace(settings.RedirectAllTo)
            ? mail.Subject
            : $"[to: {mail.ToAddress}] {mail.Subject}";

        var body = new BodyBuilder
        {
            HtmlBody = mail.HtmlBody,
            TextBody = mail.TextBody,
        };

        foreach (string path in mail.AttachmentPaths)
            if (File.Exists(path))
                await body.Attachments.AddAsync(path, ct);

        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        var security = settings.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : settings.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

        await client.ConnectAsync(settings.Host, settings.Port, security, ct);

        if (!string.IsNullOrWhiteSpace(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password ?? "", ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
