using System.Text.Json;
using Exb.Core.Configuration;
using Exb.Core.Mail;
using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Exb.Data.Services;

/// <summary>
/// The outbound mail queue.
///
/// Everything the system sends is written here first. That ordering is what
/// makes the evening pipeline safe to rehearse: with the mail provider left on
/// its default the queue simply fills up, an admin can read exactly what would
/// have been sent to whom, and no visitor is emailed by accident from a test
/// run against real registration data.
/// </summary>
public sealed class MailQueue(
    IDbContextFactory<ExhibitionDbContext> factory,
    ILogger<MailQueue> logger)
{
    public async Task<long> QueueAsync(
        string toAddress, string? toName, string subject, string htmlBody, string? textBody,
        string kind = "general", IReadOnlyList<string>? attachments = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var mail = new OutboxEmail
        {
            ToAddress = toAddress,
            ToName = toName,
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            Kind = kind,
            AttachmentsJson = JsonSerializer.Serialize(attachments ?? []),
        };

        db.OutboxEmails.Add(mail);
        await db.SaveChangesAsync(ct);
        return mail.Id;
    }

    /// <summary>
    /// Send what is waiting. Returns how many went out.
    ///
    /// Failures are retried a few times and then left alone: a permanently
    /// rejected address should show up on the Outbox screen for someone to look
    /// at, not be retried against the venue's mail server every thirty seconds
    /// for the rest of the week.
    /// </summary>
    public async Task<(int Sent, int Failed, int Held)> DispatchAsync(
        IMailTransport transport, MailSettings settings, int maxBatch = 25, CancellationToken ct = default)
    {
        const int maxAttempts = 4;

        await using var db = await factory.CreateDbContextAsync(ct);

        var pending = await db.OutboxEmails
            .Where(m => m.Status == JobStatus.Pending && m.Attempts < maxAttempts)
            .OrderBy(m => m.CreatedUtc)
            .Take(maxBatch)
            .ToListAsync(ct);

        if (pending.Count == 0) return (0, 0, 0);

        if (!transport.CanSend)
            return (0, 0, pending.Count);   // queued and waiting for a configured server

        int sent = 0, failed = 0;
        var minimumGap = settings.MaxSendsPerMinute > 0
            ? TimeSpan.FromMilliseconds(60_000.0 / settings.MaxSendsPerMinute)
            : TimeSpan.Zero;

        foreach (var mail in pending)
        {
            ct.ThrowIfCancellationRequested();
            mail.Attempts++;

            try
            {
                var attachments = JsonSerializer.Deserialize<string[]>(mail.AttachmentsJson) ?? [];
                await transport.SendAsync(
                    new OutgoingMail(mail.ToAddress, mail.ToName, mail.Subject, mail.HtmlBody, mail.TextBody, attachments),
                    ct);

                mail.Status = JobStatus.Succeeded;
                mail.SentUtc = DateTime.UtcNow;
                mail.Error = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                mail.Error = ex.Message;
                if (mail.Attempts >= maxAttempts)
                {
                    mail.Status = JobStatus.Failed;
                    logger.LogError(ex, "Giving up on email {Id} to {To} after {Attempts} attempts",
                        mail.Id, mail.ToAddress, mail.Attempts);
                }
                else
                {
                    logger.LogWarning("Email {Id} to {To} failed (attempt {Attempts}): {Message}",
                        mail.Id, mail.ToAddress, mail.Attempts, ex.Message);
                }
                failed++;
            }

            await db.SaveChangesAsync(ct);
            if (minimumGap > TimeSpan.Zero) await Task.Delay(minimumGap, ct);
        }

        return (sent, failed, 0);
    }

    public async Task<int> RetryFailedAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var failed = await db.OutboxEmails.Where(m => m.Status == JobStatus.Failed).ToListAsync(ct);

        foreach (var mail in failed)
        {
            mail.Status = JobStatus.Pending;
            mail.Attempts = 0;
            mail.Error = null;
        }

        if (failed.Count > 0) await db.SaveChangesAsync(ct);
        return failed.Count;
    }
}
