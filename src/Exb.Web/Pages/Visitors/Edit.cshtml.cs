using Exb.Core.Forms;
using Exb.Data;
using Exb.Data.Services;
using Exb.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Visitors;

public class EditModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    FormSchemaService forms,
    RegistrationService registration) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }

    public FormRenderModel Render { get; private set; } = null!;
    public string Heading { get; private set; } = "Register a visitor";
    public string? RegistrationCode { get; private set; }
    public string? AccessToken { get; private set; }
    public string? Message { get; private set; }
    public string? Problem { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await BuildAsync(null, null, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var form = await forms.GetActiveAsync(FormEntityKind.Visitor, ct);
        var submitted = Request.Form.ToDictionary(f => f.Key, f => f.Value.Select(v => v ?? "").ToArray());
        var result = FormValidator.Validate(form, submitted, adminContext: true);

        SaveOutcome outcome;
        try
        {
            outcome = await registration.SaveVisitorAsync(Id, result, User.Identity?.Name, ct);
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

        TempData["message"] = Id is null
            ? "Visitor registered. Their badge is now live on the floor."
            : "Visitor saved.";

        // A registration desk registers one person after another, so a new
        // registration returns to an empty form rather than to the record just
        // created.
        return Id is null
            ? RedirectToPage(new { id = (int?)null })
            : RedirectToPage(new { id = outcome.Id });
    }

    private async Task BuildAsync(
        Dictionary<string, string[]>? submitted,
        IReadOnlyDictionary<string, string>? errors,
        CancellationToken ct)
    {
        var form = await forms.GetActiveAsync(FormEntityKind.Visitor, ct);
        Message = TempData["message"] as string;

        var values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        await using (var lookup = await factory.CreateDbContextAsync(ct))
        {
            _categories = await lookup.Categories.AsNoTracking()
                .Where(c => c.IsActive && c.ParentId == null)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new RenderOption(c.Id.ToString(), c.Name))
                .ToListAsync(ct);

            _subCategories = await lookup.Categories.AsNoTracking()
                .Where(c => c.IsActive && c.ParentId != null)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new RenderOption(c.Id.ToString(), c.Name))
                .ToListAsync(ct);
        }

        if (Id is not null)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var visitor = await db.Visitors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == Id, ct);
            if (visitor is not null)
            {
                Heading = visitor.FullName;
                RegistrationCode = visitor.RegistrationCode;
                AccessToken = visitor.AccessToken;
                values = RegistrationService.ToFormValues(visitor, form);
            }
        }
        else
        {
            // A fresh registration should arrive with the consent boxes ticked,
            // matching what the visitor is agreeing to at the desk.
            foreach (var field in form.ActiveFields.Where(f => f.Type == FormFieldType.Checkbox
                && f.CoreProperty is "ConsentEmail" or "ConsentTracking"))
                values[field.Key] = ["true"];
        }

        if (submitted is not null)
            foreach (var (key, value) in submitted) values[key] = value;

        Render = new FormRenderModel
        {
            Form = form,
            Values = values,
            Errors = errors ?? new Dictionary<string, string>(),
            AdminContext = true,
            DynamicOptions = { ["Language"] = FormOptions.Languages },
            SourceOptions =
            {
                [FormOptionSources.Categories] = _categories,
                [FormOptionSources.SubCategories] = _subCategories,
                [FormOptionSources.Languages] = FormOptions.Languages,
                [FormOptionSources.Countries] = FormOptions.Countries,
            },
        };
    }

    private IReadOnlyList<RenderOption> _categories = [];
    private IReadOnlyList<RenderOption> _subCategories = [];
}
