using Exb.Core.Configuration;
using Exb.Core.Mail;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// Event identity, how the e-catalogue pack reaches a visitor, and how mail is
/// sent.
///
/// Both defaults are deliberately inert: packs are served from this system and
/// mail only queues. Switching either one starts sending real things to real
/// people, so the screen says so plainly rather than treating it as a
/// preference.
/// </summary>
public class DeliveryModel(
    SettingsStore settings,
    MailQueue mailQueue,
    IMailTransportSelector transports) : PageModel
{
    [BindProperty] public ExhibitionSettings Exhibition { get; set; } = new();
    [BindProperty] public DeliverySettings Delivery { get; set; } = new();
    [BindProperty] public MailSettings Mail { get; set; } = new();

    [BindProperty] public string? TestRecipient { get; set; }

    public string? Message { get; private set; }
    public IReadOnlyList<string> Problems { get; private set; } = [];
    public bool BaseUrlLooksLocal { get; private set; }

    public void OnGet()
    {
        Load();
        Message = TempData["message"] as string;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Exhibition.Name))
            problems.Add("The exhibition needs a name; it appears on every report and QR page.");

        if (!Uri.TryCreate(Exhibition.PublicBaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
            problems.Add("The public base URL must be an absolute http or https address, because printed QR codes resolve to it.");

        if (Delivery.Provider.Equals("wetransfer", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(Delivery.WeTransfer.ApiKey))
            problems.Add("WeTransfer is selected but has no API key, so packs would fall back to a local link.");

        if (Delivery.Provider.Equals("generic", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(Delivery.Generic.UploadUrl))
            problems.Add("The generic transfer provider is selected but has no upload URL.");

        if (Mail.Provider.Equals("smtp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Mail.Host))
                problems.Add("SMTP is selected but no server host is set.");
            if (string.IsNullOrWhiteSpace(Mail.FromAddress))
                problems.Add("SMTP needs a from-address.");
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(Exhibition.TimeZoneId);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            problems.Add($"'{Exhibition.TimeZoneId}' is not a time zone this server knows. "
                + "It decides when a visit belongs to today rather than yesterday.");
        }

        if (problems.Count > 0)
        {
            Problems = problems;
            BaseUrlLooksLocal = LooksLocal(Exhibition.PublicBaseUrl);
            return Page();
        }

        await settings.SaveAsync(SettingsKeys.Exhibition, Exhibition, User.Identity?.Name, ct);
        await settings.SaveAsync(SettingsKeys.Delivery, Delivery, User.Identity?.Name, ct);
        await settings.SaveAsync(SettingsKeys.Mail, Mail, User.Identity?.Name, ct);

        TempData["message"] = Mail.Provider.Equals("smtp", StringComparison.OrdinalIgnoreCase)
            ? "Saved. Mail will now be delivered to visitors over SMTP."
            : "Saved. Mail is still held in the Outbox and nothing is being delivered.";

        return RedirectToPage();
    }

    /// <summary>Queue one message to a named address, to prove the whole path works before the show.</summary>
    public async Task<IActionResult> OnPostTestAsync(CancellationToken ct)
    {
        Load();

        if (string.IsNullOrWhiteSpace(TestRecipient))
        {
            TempData["message"] = "Enter an address to send the test to.";
            return RedirectToPage();
        }

        long id = await mailQueue.QueueAsync(
            TestRecipient.Trim(),
            "Test recipient",
            $"{Exhibition.Name}: delivery test",
            "<p>This is a test message from the exhibition tracking system. "
            + "If it reached you, the evening reports and e-catalogue packs will too.</p>",
            "This is a test message from the exhibition tracking system.",
            kind: "test",
            ct: ct);

        var transport = transports.Resolve(settings.Current.Mail);
        TempData["message"] = transport.CanSend
            ? $"Test message #{id} queued and will be sent within a minute."
            : $"Test message #{id} written to the Outbox. It will not be sent while the mail provider is '{settings.Current.Mail.Provider}'.";

        return RedirectToPage();
    }

    private void Load()
    {
        var current = settings.Current;
        Exhibition = current.Exhibition.Clone();
        Delivery = current.Delivery.Clone();
        Mail = current.Mail.Clone();
        BaseUrlLooksLocal = LooksLocal(Exhibition.PublicBaseUrl);
    }

    private static bool LooksLocal(string? url)
        => url is not null
           && (url.Contains("localhost", StringComparison.OrdinalIgnoreCase) || url.Contains("127.0.0.1"));
}
