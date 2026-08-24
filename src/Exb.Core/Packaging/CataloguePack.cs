using System.IO.Compression;
using System.Text;
using Exb.Core.Text;

namespace Exb.Core.Packaging;

/// <summary>One file an exhibitor has published as their e-catalogue.</summary>
public sealed record PackFile(string FileName, string ContentType, string SourcePath);

/// <summary>One exhibitor's contribution to a visitor's evening pack.</summary>
public sealed record PackItem(
    int ExhibitorId,
    string ExhibitorName,
    string StandNumber,
    string HallName,
    string? CategoryName,
    string? SubCategoryName,
    string? Website,
    string? Email,
    string? Summary,
    DateTime RequestedUtc,
    IReadOnlyList<PackFile> Files);

public sealed record PackResult(
    string ZipPath,
    long SizeBytes,
    int ItemCount,
    int FileCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Builds the single zip a visitor receives at the end of the day, holding every
/// e-catalogue they scanned.
///
/// Two decisions are worth stating. Exhibitors who have not uploaded a catalogue
/// still get a folder, containing a generated stand sheet with their details:
/// the visitor asked for that company's information and a silently missing
/// folder reads as a system failure rather than as an exhibitor's omission. And
/// the pack always carries an index page, because a zip of thirty PDFs named
/// after their originating agencies is nearly unusable a week later.
/// </summary>
public sealed class CataloguePackBuilder
{
    /// <summary>Stored rather than deflated for files that are already compressed.</summary>
    private static readonly HashSet<string> AlreadyCompressed =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".zip", ".mp4", ".gif", ".docx", ".xlsx", ".pptx" };

    public PackResult Build(
        string zipPath,
        string visitorName,
        string exhibitionName,
        DateOnly eventDate,
        IReadOnlyList<PackItem> items)
    {
        var warnings = new List<string>();
        int fileCount = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            int index = 1;
            foreach (var item in items.OrderBy(i => i.ExhibitorName, StringComparer.OrdinalIgnoreCase))
            {
                string folder = $"{index:D2} {Html.SafeFileName(item.ExhibitorName, $"exhibitor-{item.ExhibitorId}")}";
                index++;

                bool addedAny = false;
                foreach (var file in item.Files)
                {
                    if (!File.Exists(file.SourcePath))
                    {
                        warnings.Add($"{item.ExhibitorName}: '{file.FileName}' is missing from storage and was skipped.");
                        continue;
                    }

                    string entryName = $"{folder}/{Html.SafeFileName(Path.GetFileNameWithoutExtension(file.FileName), "catalogue")}{Path.GetExtension(file.FileName)}";
                    var level = AlreadyCompressed.Contains(Path.GetExtension(file.FileName))
                        ? CompressionLevel.NoCompression
                        : CompressionLevel.Optimal;

                    zip.CreateEntryFromFile(file.SourcePath, entryName, level);
                    fileCount++;
                    addedAny = true;
                }

                // Always leave the visitor something for every stand they scanned.
                WriteTextEntry(zip, $"{folder}/{Html.SafeFileName(item.ExhibitorName, "stand")} - stand details.html", StandSheet(item, exhibitionName));
                if (!addedAny)
                    warnings.Add($"{item.ExhibitorName} has not published an e-catalogue; a stand details sheet was included instead.");
            }

            WriteTextEntry(zip, "index.html", IndexPage(visitorName, exhibitionName, eventDate, items));
            WriteTextEntry(zip, "README.txt", ReadMe(visitorName, exhibitionName, eventDate, items.Count, fileCount));
        }

        var info = new FileInfo(zipPath);
        return new PackResult(zipPath, info.Length, items.Count, fileCount, warnings);
    }

    private static void WriteTextEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string IndexPage(string visitorName, string exhibitionName, DateOnly date, IReadOnlyList<PackItem> items)
    {
        var rows = new StringBuilder();
        int index = 1;

        foreach (var item in items.OrderBy(i => i.ExhibitorName, StringComparer.OrdinalIgnoreCase))
        {
            string folder = $"{index:D2} {Html.SafeFileName(item.ExhibitorName, $"exhibitor-{item.ExhibitorId}")}";
            index++;

            string website = Html.SafeUrl(item.Website) is { } url
                ? $"<a href=\"{Html.Escape(url)}\">{Html.Escape(item.Website)}</a>"
                : "&mdash;";

            string files = item.Files.Count == 0
                ? "<em>stand details sheet</em>"
                : string.Join("<br>", item.Files.Select(f =>
                    $"<a href=\"{Uri.EscapeDataString(folder)}/{Uri.EscapeDataString(Html.SafeFileName(Path.GetFileNameWithoutExtension(f.FileName), "catalogue") + Path.GetExtension(f.FileName))}\">{Html.Escape(f.FileName)}</a>"));

            rows.Append($"""
                <tr>
                  <td class="stand">{Html.Escape(item.StandNumber)}</td>
                  <td><strong>{Html.Escape(item.ExhibitorName)}</strong><br><span class="muted">{Html.Escape(item.HallName)}</span></td>
                  <td>{Html.Escape(item.CategoryName)}{(string.IsNullOrEmpty(item.SubCategoryName) ? "" : " &rsaquo; " + Html.Escape(item.SubCategoryName))}</td>
                  <td>{files}</td>
                  <td>{website}</td>
                </tr>

                """);
        }

        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <title>E-catalogues - {Html.Escape(exhibitionName)}</title>
            {PackStyles}
            </head><body>
            <h1>Your e-catalogues</h1>
            <p class="lead">{Html.Escape(exhibitionName)} &middot; {date:dd MMMM yyyy}<br>
            Collected for {Html.Escape(visitorName)} &middot; {items.Count} exhibitor(s)</p>
            <table>
              <thead><tr><th>Stand</th><th>Exhibitor</th><th>Category</th><th>Files</th><th>Website</th></tr></thead>
              <tbody>
            {rows}  </tbody>
            </table>
            <p class="muted">Open this file from inside the unzipped folder for the links to work.</p>
            </body></html>
            """;
    }

    private static string StandSheet(PackItem item, string exhibitionName)
    {
        string website = Html.SafeUrl(item.Website) is { } url
            ? $"<a href=\"{Html.Escape(url)}\">{Html.Escape(item.Website)}</a>"
            : "&mdash;";
        string email = string.IsNullOrWhiteSpace(item.Email)
            ? "&mdash;"
            : $"<a href=\"mailto:{Html.Escape(item.Email)}\">{Html.Escape(item.Email)}</a>";

        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <title>{Html.Escape(item.ExhibitorName)}</title>
            {PackStyles}
            </head><body>
            <h1>{Html.Escape(item.ExhibitorName)}</h1>
            <p class="lead">{Html.Escape(exhibitionName)} &middot; {Html.Escape(item.HallName)} &middot; Stand {Html.Escape(item.StandNumber)}</p>
            <table>
              <tr><th>Category</th><td>{Html.Escape(item.CategoryName)}</td></tr>
              <tr><th>Sub-category</th><td>{Html.Escape(item.SubCategoryName)}</td></tr>
              <tr><th>Website</th><td>{website}</td></tr>
              <tr><th>Email</th><td>{email}</td></tr>
              <tr><th>Requested</th><td>{item.RequestedUtc:dd MMM yyyy HH:mm} UTC</td></tr>
            </table>
            {(string.IsNullOrWhiteSpace(item.Summary) ? "" : $"<h2>About</h2><p>{Html.Escape(item.Summary)}</p>")}
            </body></html>
            """;
    }

    private static string ReadMe(string visitorName, string exhibitionName, DateOnly date, int items, int files)
        => $"""
            {exhibitionName}
            E-catalogue pack for {visitorName}
            {date:dd MMMM yyyy}

            {items} exhibitor(s), {files} catalogue file(s).

            Open index.html for a table of every stand you scanned, with links to
            each file and the exhibitor's contact details.

            Every exhibitor you scanned has a numbered folder. Where an exhibitor
            has not published a catalogue, the folder holds a stand details sheet
            instead.
            """;

    private const string PackStyles = """
        <style>
          body { font: 15px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; color: #1c2530; max-width: 900px; margin: 32px auto; padding: 0 20px; }
          h1 { font-size: 22px; margin: 0 0 4px; }
          h2 { font-size: 16px; margin: 24px 0 8px; }
          .lead { color: #5b6b7d; margin: 0 0 24px; }
          .muted { color: #8194aa; font-size: 13px; }
          table { border-collapse: collapse; width: 100%; }
          th, td { text-align: left; padding: 9px 12px; border-bottom: 1px solid #e3e8ee; vertical-align: top; }
          th { background: #f6f8fa; font-size: 12px; text-transform: uppercase; letter-spacing: .4px; color: #5b6b7d; }
          td.stand { font-family: ui-monospace, Consolas, monospace; font-weight: 600; white-space: nowrap; }
          a { color: #1667c4; }
        </style>
        """;
}
