using Exb.Core.Forms;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Exhibitors;

public class EditModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    FormSchemaService forms,
    RegistrationService registration,
    CatalogueStorage storage) : PageModel
{
    /// <summary>Refused rather than trusted: these are the shapes that would let an upload run as code.</summary>
    private static readonly string[] BlockedExtensions =
        [".exe", ".dll", ".bat", ".cmd", ".com", ".ps1", ".sh", ".js", ".vbs", ".jar", ".msi", ".scr", ".htm", ".html"];

    private const long MaxUploadBytes = 80 * 1024 * 1024;

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }

    public FormRenderModel Render { get; private set; } = null!;
    public string CompanyName { get; private set; } = "New exhibitor";
    public IReadOnlyList<CatalogueAsset> Catalogues { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Problem { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await BuildAsync(null, null, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var form = await forms.GetActiveAsync(FormEntityKind.Exhibitor, ct);
        var submitted = Request.Form.ToDictionary(f => f.Key, f => f.Value.Select(v => v ?? "").ToArray());
        var result = FormValidator.Validate(form, submitted, adminContext: true);

        SaveOutcome outcome;
        try
        {
            outcome = await registration.SaveExhibitorAsync(Id, result, User.Identity?.Name, ct);
        }
        catch (InvalidOperationException ex)
        {
            await BuildAsync(submitted, null, ct);
            Problem = ex.Message;
            return Page();
        }

        if (!outcome.Saved)
        {
            await BuildAsync(submitted, outcome.Errors, ct);
            Problem = "Some answers need attention.";
            return Page();
        }

        TempData["message"] = Id is null ? "Exhibitor created. Now place their stand." : "Exhibitor saved.";
        return Id is null
            ? RedirectToPage("/Exhibitors/Stand", new { id = outcome.Id })
            : RedirectToPage(new { id = outcome.Id });
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (Id is null) return RedirectToPage("/Exhibitors/Index");

        if (file is null || file.Length == 0)
        {
            TempData["problem"] = "No file was selected.";
            return RedirectToPage(new { id = Id });
        }

        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (BlockedExtensions.Contains(extension))
        {
            TempData["problem"] = $"'{extension}' files are not accepted as catalogues.";
            return RedirectToPage(new { id = Id });
        }

        if (file.Length > MaxUploadBytes)
        {
            TempData["problem"] = $"That file is {file.Length / 1024 / 1024} MB; the limit is {MaxUploadBytes / 1024 / 1024} MB.";
            return RedirectToPage(new { id = Id });
        }

        // The stored name is derived, never taken from the upload: a filename is
        // attacker-controlled and would otherwise decide where the bytes land.
        string safeName = Core.Text.Html.SafeFileName(Path.GetFileNameWithoutExtension(file.FileName), "catalogue") + extension;
        string path = storage.CataloguePathFor(Id.Value, $"{Tokens.New(6)}-{safeName}");

        await using (var stream = System.IO.File.Create(path))
            await file.CopyToAsync(stream, ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        db.CatalogueAssets.Add(new CatalogueAsset
        {
            ExhibitorId = Id.Value,
            FileName = safeName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            StoragePath = storage.ToRelative(path),
        });
        await db.SaveChangesAsync(ct);

        TempData["message"] = $"'{safeName}' added to this exhibitor's e-catalogue.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostRemoveFileAsync(int assetId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var asset = await db.CatalogueAssets.FirstOrDefaultAsync(a => a.Id == assetId, ct);

        if (asset is not null)
        {
            // Retired rather than deleted: packs already built and emailed refer
            // to this file, and the row is the record of what was sent.
            asset.IsActive = false;
            await db.SaveChangesAsync(ct);
            TempData["message"] = $"'{asset.FileName}' removed from future packs.";
        }

        return RedirectToPage(new { id = Id });
    }

    private async Task BuildAsync(
        Dictionary<string, string[]>? submitted,
        IReadOnlyDictionary<string, string>? errors,
        CancellationToken ct)
    {
        var form = await forms.GetActiveAsync(FormEntityKind.Exhibitor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);

        Message = TempData["message"] as string;
        Problem ??= TempData["problem"] as string;

        var categories = await db.Categories.AsNoTracking()
            .OrderBy(c => c.ParentId == null ? 0 : 1).ThenBy(c => c.DisplayOrder)
            .Select(c => new { c.Id, c.Name, c.ParentId })
            .ToListAsync(ct);

        var values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (Id is not null)
        {
            var exhibitor = await db.Exhibitors.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Id, ct);
            if (exhibitor is not null)
            {
                CompanyName = exhibitor.CompanyName;
                values = RegistrationService.ToFormValues(exhibitor, form);
            }

            Catalogues = await db.CatalogueAssets.AsNoTracking()
                .Where(a => a.ExhibitorId == Id && a.IsActive)
                .OrderBy(a => a.FileName)
                .ToListAsync(ct);
        }

        if (submitted is not null)
            foreach (var (key, value) in submitted) values[key] = value;

        Render = new FormRenderModel
        {
            Form = form,
            Values = values,
            Errors = errors ?? new Dictionary<string, string>(),
            AdminContext = true,
            DynamicOptions =
            {
                ["CategoryId"] = categories.Where(c => c.ParentId is null)
                    .Select(c => new RenderOption(c.Id.ToString(), c.Name)).ToList(),
                ["SubCategoryId"] = categories.Where(c => c.ParentId is not null)
                    .Select(c => new RenderOption(
                        c.Id.ToString(),
                        $"{categories.FirstOrDefault(p => p.Id == c.ParentId)?.Name} › {c.Name}"))
                    .ToList(),
            },
            SourceOptions =
            {
                [FormOptionSources.Categories] = categories.Where(c => c.ParentId is null)
                    .Select(c => new RenderOption(c.Id.ToString(), c.Name)).ToList(),
                [FormOptionSources.SubCategories] = categories.Where(c => c.ParentId is not null)
                    .Select(c => new RenderOption(c.Id.ToString(), c.Name)).ToList(),
                [FormOptionSources.Languages] = FormOptions.Languages,
                [FormOptionSources.Countries] = FormOptions.Countries,
            },
        };
    }
}
