using System.Text;

namespace Exb.Core.Text;

/// <summary>
/// Escaping for the HTML this project builds outside Razor: the daily report
/// email, the e-catalogue pack's index page and the generated stand sheets.
/// Those strings come from exhibitor and visitor form input, so they get escaped
/// on the way out rather than trusted.
/// </summary>
public static class Html
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var sb = new StringBuilder(value.Length + 16);
        foreach (char c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Only let through links we would be willing to put in an email. Exhibitor
    /// websites are typed into a form by a third party, so a javascript: or
    /// data: URL must never reach a visitor's inbox.
    /// </summary>
    public static string? SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string trimmed = url.Trim();

        if (!trimmed.Contains("://") && !trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            trimmed = "https://" + trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;

        return uri.Scheme is "http" or "https" or "mailto" ? uri.ToString() : null;
    }

    /// <summary>Turn a company name into something safe to use as a folder name inside a zip.</summary>
    public static string SafeFileName(string? name, string fallback = "item")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var sb = new StringBuilder(name.Length);
        foreach (char c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '&' or '(' or ')')
                sb.Append(c);
            else
                sb.Append('-');
        }

        string cleaned = sb.ToString().Trim(' ', '.', '-');
        while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");

        if (cleaned.Length > 80) cleaned = cleaned[..80].TrimEnd(' ', '.', '-');
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
