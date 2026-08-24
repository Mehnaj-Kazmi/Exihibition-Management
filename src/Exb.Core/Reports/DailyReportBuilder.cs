using System.Text;
using Exb.Core.Configuration;
using Exb.Core.Dwell;
using Exb.Core.Interest;
using Exb.Core.Text;

namespace Exb.Core.Reports;

public sealed record ReportRecipient(int VisitorId, string FullName, string Email, string? Company);

public sealed record PackLink(string Url, DateTime? ExpiresUtc, int ExhibitorCount, long SizeBytes);

public sealed record BuiltReport(string Subject, string Html, string TextBody);

/// <summary>
/// Renders one visitor's end-of-day email.
///
/// Written as inline-styled tables rather than a modern stylesheet because it
/// has to survive Outlook, which still ignores most of what a browser would do
/// with it. The structure follows what the visitor actually wants to know, in
/// order: what you were interested in, what you asked for, and what you missed.
///
/// The methodology footer is not boilerplate. The report tells someone how long
/// they stood in front of a stranger's stand, inferred from radio measurements,
/// so it says plainly how that was worked out and what the thresholds were.
/// </summary>
public sealed class DailyReportBuilder
{
    private const string Ink = "#1c2530";
    private const string Muted = "#6b7c8f";
    private const string Line = "#e3e8ee";
    private const string Accent = "#125ea8";
    private const string Wash = "#f6f8fa";

    public BuiltReport Build(
        ReportRecipient visitor,
        VisitorDayProfile profile,
        ExhibitionSettings exhibition,
        DwellSettings dwell,
        PackLink? pack)
    {
        string subject = profile.HasInterest
            ? $"{exhibition.Name}: your {profile.EventDate:d MMMM} summary and {profile.Missed.Count} stands you may have missed"
            : $"{exhibition.Name}: your {profile.EventDate:d MMMM} summary";

        var html = new StringBuilder(16 * 1024);

        html.Append($"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{Html.Escape(subject)}</title></head>
            <body style="margin:0;padding:0;background:#eef2f6;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#eef2f6;padding:24px 12px;">
            <tr><td align="center">
            <table role="presentation" width="720" cellpadding="0" cellspacing="0" style="width:100%;max-width:720px;background:#ffffff;border-radius:10px;overflow:hidden;font-family:system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};">

            <tr><td style="background:{Accent};padding:22px 26px;color:#ffffff;">
              <div style="font-size:19px;font-weight:600;">{Html.Escape(exhibition.Name)}</div>
              <div style="font-size:13px;opacity:.85;margin-top:3px;">Your visit summary &middot; {profile.EventDate:dddd d MMMM yyyy}</div>
            </td></tr>

            <tr><td style="padding:24px 26px 6px;">
              <p style="margin:0 0 14px;font-size:15px;">Dear {Html.Escape(FirstName(visitor.FullName))},</p>
              {Opening(profile)}
            </td></tr>
            """);

        AppendSummaryTiles(html, profile);
        if (pack is not null) AppendPack(html, pack);
        if (profile.Visited.Count > 0) AppendVisited(html, profile);
        if (profile.Categories.Count > 0) AppendCategories(html, profile);
        if (profile.Missed.Count > 0) AppendMissed(html, profile, exhibition);

        html.Append($"""
            <tr><td style="padding:6px 26px 26px;">
              <div style="border-top:1px solid {Line};padding-top:14px;font-size:11.5px;line-height:1.6;color:{Muted};">
                <strong style="color:{Ink};">How this was measured.</strong>
                Your entry badge carries a passive RFID tag. Antennas mounted on each stand
                record how long the badge stayed within range, and that dwell time is what
                appears above &mdash; nothing you said or did was recorded. A stop of
                {dwell.MinDwellSeconds} seconds or more counts as a visit,
                {dwell.InterestSeconds} seconds as interest, and
                {dwell.StrongSeconds / 60} minutes or more as strong interest. Where two
                neighbouring stands were too close to tell apart, the visit is reported one
                level lower rather than guessed.
                <br><br>
                You are receiving this because you consented to visit tracking at
                registration. Reply to this email to have your data removed.
                <br><br>
                {Html.Escape(exhibition.OrganiserName)}{(string.IsNullOrWhiteSpace(exhibition.Venue) ? "" : " &middot; " + Html.Escape(exhibition.Venue))}
              </div>
            </td></tr>

            </table></td></tr></table></body></html>
            """);

        return new BuiltReport(subject, html.ToString(), BuildText(visitor, profile, exhibition, pack));
    }

    private static string Opening(VisitorDayProfile profile)
    {
        if (!profile.HasInterest && profile.Visited.Count == 0)
            return """<p style="margin:0 0 8px;font-size:15px;">We did not record any stand visits for your badge today. If that looks wrong, the registration desk can check your badge.</p>""";

        string top = profile.Categories.Count > 0
            ? $" Most of that went to <strong>{Html.Escape(profile.Categories[0].CategoryName)}</strong>."
            : "";

        return $"""
            <p style="margin:0 0 8px;font-size:15px;">
              You spent <strong>{profile.TotalDwellText}</strong> across
              <strong>{profile.Visited.Count}</strong> stand(s) today.{top}
              Below is where your time went, and the stands in those same categories you did not reach.
            </p>
            """;
    }

    private void AppendSummaryTiles(StringBuilder html, VisitorDayProfile profile)
    {
        (string Value, string Label)[] tiles =
        [
            (profile.StandsWithInterest.ToString(), "stands of interest"),
            (profile.TotalDwellText, "total time on stands"),
            (profile.Categories.Count.ToString(), "categories"),
            (profile.Missed.Count.ToString(), "stands missed"),
        ];

        html.Append($"""<tr><td style="padding:8px 26px 4px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>""");
        foreach (var (value, label) in tiles)
        {
            html.Append($"""
                <td width="25%" style="padding:4px;">
                  <div style="background:{Wash};border:1px solid {Line};border-radius:8px;padding:12px 10px;text-align:center;">
                    <div style="font-size:21px;font-weight:650;color:{Accent};">{Html.Escape(value)}</div>
                    <div style="font-size:10.5px;text-transform:uppercase;letter-spacing:.5px;color:{Muted};margin-top:2px;">{label}</div>
                  </div>
                </td>
                """);
        }
        html.Append("</tr></table></td></tr>");
    }

    private void AppendPack(StringBuilder html, PackLink pack)
    {
        string expiry = pack.ExpiresUtc is null
            ? ""
            : $"""<div style="font-size:11.5px;color:{Muted};margin-top:8px;">This link stays available until {pack.ExpiresUtc:d MMMM yyyy}.</div>""";

        html.Append($"""
            <tr><td style="padding:16px 26px 4px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#eef6ff;border:1px solid #cfe2fa;border-radius:8px;">
              <tr><td style="padding:16px 18px;">
                <div style="font-size:15px;font-weight:600;margin-bottom:4px;">Your e-catalogues are ready</div>
                <div style="font-size:13.5px;color:{Muted};margin-bottom:12px;">
                  {pack.ExhibitorCount} exhibitor(s) you scanned today, in one download ({Megabytes(pack.SizeBytes)}).
                </div>
                <a href="{Html.Escape(pack.Url)}" style="display:inline-block;background:{Accent};color:#ffffff;text-decoration:none;font-size:14px;font-weight:600;padding:10px 20px;border-radius:6px;">Download the pack</a>
                {expiry}
              </td></tr></table>
            </td></tr>
            """);
    }

    private void AppendVisited(StringBuilder html, VisitorDayProfile profile)
    {
        var rows = new StringBuilder();
        foreach (var v in profile.Visited)
        {
            rows.Append($"""
                <tr>
                  <td style="{Cell}font-family:ui-monospace,Consolas,monospace;font-weight:600;white-space:nowrap;">{Html.Escape(v.Kiosk.StandNumber)}</td>
                  <td style="{Cell}"><strong>{Html.Escape(v.Kiosk.ExhibitorName)}</strong><br>
                      <span style="font-size:11.5px;color:{Muted};">{Html.Escape(v.Kiosk.HallName)} &middot; Zone {Html.Escape(v.Kiosk.Zone)}</span></td>
                  <td style="{Cell}font-size:12.5px;">{Html.Escape(v.Kiosk.CategoryName)}{Sub(v.Kiosk.SubCategoryName)}</td>
                  <td style="{Cell}white-space:nowrap;">{Html.Escape(v.DwellText)}</td>
                  <td style="{Cell}white-space:nowrap;">{LevelBadge(v.Level)}</td>
                  <td style="{Cell}text-align:center;">{(v.CatalogueRequested ? "&#10003;" : "&mdash;")}</td>
                </tr>
                """);
        }

        html.Append($"""
            <tr><td style="padding:20px 26px 4px;">
              <h2 style="font-size:15px;margin:0 0 10px;">Where you spent your time</h2>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13.5px;">
                <thead><tr>
                  <th style="{Head}">Stand</th><th style="{Head}">Exhibitor</th><th style="{Head}">Category</th>
                  <th style="{Head}">Time</th><th style="{Head}">Level</th><th style="{Head}">Catalogue</th>
                </tr></thead>
                <tbody>{rows}</tbody>
              </table>
            </td></tr>
            """);
    }

    private void AppendCategories(StringBuilder html, VisitorDayProfile profile)
    {
        var rows = new StringBuilder();
        foreach (var c in profile.Categories)
        {
            int width = (int)Math.Round(Math.Clamp(c.SharePct, 2, 100));
            string subs = c.SubCategories.Count == 0
                ? ""
                : $"""<div style="font-size:11.5px;color:{Muted};margin-top:3px;">{Html.Escape(string.Join(", ", c.SubCategories.Select(s => s.SubCategoryName)))}</div>""";

            rows.Append($"""
                <tr>
                  <td style="{Cell}width:44%;"><strong>{Html.Escape(c.CategoryName)}</strong>{subs}</td>
                  <td style="{Cell}white-space:nowrap;">{Html.Escape(c.DwellText)}</td>
                  <td style="{Cell}white-space:nowrap;">{c.StandCount} stand(s)</td>
                  <td style="{Cell}width:34%;">
                    <div style="background:{Line};border-radius:3px;height:8px;">
                      <div style="background:{Accent};width:{width}%;height:8px;border-radius:3px;"></div>
                    </div>
                    <div style="font-size:11px;color:{Muted};margin-top:2px;">{c.SharePct:0.#}% of your time</div>
                  </td>
                </tr>
                """);
        }

        html.Append($"""
            <tr><td style="padding:20px 26px 4px;">
              <h2 style="font-size:15px;margin:0 0 10px;">Your interests today</h2>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13.5px;">
                <tbody>{rows}</tbody>
              </table>
            </td></tr>
            """);
    }

    private void AppendMissed(StringBuilder html, VisitorDayProfile profile, ExhibitionSettings exhibition)
    {
        var rows = new StringBuilder();
        foreach (var m in profile.Missed)
        {
            string website = Html.SafeUrl(m.Kiosk.Website) is { } url
                ? $"""<a href="{Html.Escape(url)}" style="color:{Accent};">{Html.Escape(ShortHost(m.Kiosk.Website))}</a>"""
                : "&mdash;";

            rows.Append($"""
                <tr>
                  <td style="{Cell}font-family:ui-monospace,Consolas,monospace;font-weight:600;white-space:nowrap;">{Html.Escape(m.Kiosk.StandNumber)}</td>
                  <td style="{Cell}"><strong>{Html.Escape(m.Kiosk.ExhibitorName)}</strong>
                      {(string.IsNullOrWhiteSpace(m.Kiosk.Summary) ? "" : $"""<br><span style="font-size:11.5px;color:{Muted};">{Html.Escape(Trim(m.Kiosk.Summary, 110))}</span>""")}</td>
                  <td style="{Cell}font-size:12.5px;">{Html.Escape(m.Kiosk.CategoryName)}{Sub(m.Kiosk.SubCategoryName)}</td>
                  <td style="{Cell}font-size:12.5px;white-space:nowrap;">{Html.Escape(m.Kiosk.HallName)}<br>
                      <span style="color:{Muted};">Zone {Html.Escape(m.Kiosk.Zone)}</span></td>
                  <td style="{Cell}font-size:12.5px;">{website}</td>
                </tr>
                """);
        }

        html.Append($"""
            <tr><td style="padding:20px 26px 4px;">
              <h2 style="font-size:15px;margin:0 0 4px;">Stands you may have missed</h2>
              <p style="margin:0 0 10px;font-size:12.5px;color:{Muted};">
                Exhibitors in the categories you spent the most time on, whose stands your badge never reached.
                {(profile.EventDate < DateOnly.FromDateTime(DateTime.Today) ? "" : "There is still time tomorrow.")}
              </p>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13.5px;">
                <thead><tr>
                  <th style="{Head}">Stand</th><th style="{Head}">Exhibitor</th><th style="{Head}">Category</th>
                  <th style="{Head}">Location</th><th style="{Head}">Website</th>
                </tr></thead>
                <tbody>{rows}</tbody>
              </table>
            </td></tr>
            """);
    }

    // --- plain text alternative ---------------------------------------------

    private static string BuildText(
        ReportRecipient visitor, VisitorDayProfile profile, ExhibitionSettings exhibition, PackLink? pack)
    {
        var sb = new StringBuilder();
        sb.AppendLine(exhibition.Name);
        sb.AppendLine($"Your visit summary - {profile.EventDate:dddd d MMMM yyyy}");
        sb.AppendLine();
        sb.AppendLine($"Dear {FirstName(visitor.FullName)},");
        sb.AppendLine();
        sb.AppendLine($"You spent {profile.TotalDwellText} across {profile.Visited.Count} stand(s).");
        sb.AppendLine();

        if (pack is not null)
        {
            sb.AppendLine($"YOUR E-CATALOGUES ({pack.ExhibitorCount} exhibitors)");
            sb.AppendLine(pack.Url);
            sb.AppendLine();
        }

        if (profile.Visited.Count > 0)
        {
            sb.AppendLine("WHERE YOU SPENT YOUR TIME");
            foreach (var v in profile.Visited)
                sb.AppendLine($"  {v.Kiosk.StandNumber,-8} {v.Kiosk.ExhibitorName,-38} {v.DwellText,-12} {InterestFormatting.LevelText(v.Level)}");
            sb.AppendLine();
        }

        if (profile.Missed.Count > 0)
        {
            sb.AppendLine("STANDS YOU MAY HAVE MISSED");
            foreach (var m in profile.Missed)
                sb.AppendLine($"  {m.Kiosk.StandNumber,-8} {m.Kiosk.ExhibitorName,-38} {m.Kiosk.CategoryName} ({m.Kiosk.HallName}, zone {m.Kiosk.Zone})");
            sb.AppendLine();
        }

        sb.AppendLine($"{exhibition.OrganiserName}");
        return sb.ToString();
    }

    // --- small helpers -------------------------------------------------------

    private const string Cell = "padding:8px 10px;border-bottom:1px solid #e3e8ee;vertical-align:top;";
    private const string Head = "padding:7px 10px;background:#f6f8fa;border-bottom:1px solid #e3e8ee;text-align:left;font-size:10.5px;text-transform:uppercase;letter-spacing:.4px;color:#6b7c8f;";

    private static string Sub(string? sub)
        => string.IsNullOrWhiteSpace(sub) ? "" : $"<br><span style=\"color:{Muted};\">{Html.Escape(sub)}</span>";

    private static string LevelBadge(DwellLevel level)
    {
        var (bg, fg) = level switch
        {
            DwellLevel.Strong => ("#e6f6ec", "#1a7f43"),
            DwellLevel.Interested => ("#eef6ff", "#125ea8"),
            DwellLevel.Browsed => ("#f6f8fa", "#6b7c8f"),
            _ => ("#f6f8fa", "#8194aa"),
        };
        return $"""<span style="display:inline-block;background:{bg};color:{fg};font-size:11px;font-weight:600;padding:3px 8px;border-radius:10px;">{InterestFormatting.LevelText(level)}</span>""";
    }

    private static string FirstName(string full)
    {
        string trimmed = (full ?? "").Trim();
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed.Length == 0 ? "visitor" : trimmed;
    }

    private static string ShortHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        string s = url.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        return s.StartsWith("www.") ? s[4..] : s;
    }

    private static string Trim(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private static string Megabytes(long bytes)
        => bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / (1024.0 * 1024.0):0.#} MB";
}
