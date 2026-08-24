using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// Everything the system has decided to send, whether or not it has been sent.
///
/// With the default mail provider nothing leaves the building, and this screen is
/// where an organiser reads exactly what would have gone out and to whom, before
/// switching delivery on.
/// </summary>
public class OutboxModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    MailQueue queue,
    SettingsStore settings,
    IMailTransportSelector transports) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public long? Preview { get; set; }

    public IReadOnlyList<OutboxEmail> Rows { get; private set; } = [];
    public OutboxEmail? PreviewMail { get; private set; }
    public int Pending { get; private set; }
    public int Sent { get; private set; }
    public int Failed { get; private set; }
    public bool CanSend { get; private set; }
    public string Provider { get; private set; } = "";
    public string? Message { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostSendNowAsync(CancellationToken ct)
    {
        var mailSettings = settings.Current.Mail;
        var transport = transports.Resolve(mailSettings);
        var (sent, failed, held) = await queue.DispatchAsync(transport, mailSettings, 100, ct);

        TempData["message"] = held > 0
            ? $"{held} message(s) are waiting, but the mail provider is '{mailSettings.Provider}' so nothing was sent."
            : $"{sent} sent, {failed} failed.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(CancellationToken ct)
    {
        int count = await queue.RetryFailedAsync(ct);
        TempData["message"] = $"{count} failed message(s) put back in the queue.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetBodyAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var mail = await db.OutboxEmails.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mail is null) return NotFound();

        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";
        return Content(mail.HtmlBody, "text/html; charset=utf-8");
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Message = TempData["message"] as string;
        Provider = settings.Current.Mail.Provider;
        CanSend = transports.Resolve(settings.Current.Mail).CanSend;

        await using var db = await factory.CreateDbContextAsync(ct);

        Pending = await db.OutboxEmails.CountAsync(m => m.Status == JobStatus.Pending, ct);
        Sent = await db.OutboxEmails.CountAsync(m => m.Status == JobStatus.Succeeded, ct);
        Failed = await db.OutboxEmails.CountAsync(m => m.Status == JobStatus.Failed, ct);

        var query = db.OutboxEmails.AsNoTracking();
        query = Status switch
        {
            "pending" => query.Where(m => m.Status == JobStatus.Pending),
            "sent" => query.Where(m => m.Status == JobStatus.Succeeded),
            "failed" => query.Where(m => m.Status == JobStatus.Failed),
            _ => query,
        };

        Rows = await query.OrderByDescending(m => m.Id).Take(200).ToListAsync(ct);

        if (Preview is not null)
            PreviewMail = await db.OutboxEmails.AsNoTracking().FirstOrDefaultAsync(m => m.Id == Preview, ct);
    }
}
