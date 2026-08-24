using Exb.Core.Forms;
using Exb.Data.Entities;
using Exb.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// The visitor and exhibitor form layouts, and their version history.
///
/// Every save creates a new version rather than overwriting, so an organiser who
/// breaks the registration form at nine on opening morning can put the previous
/// one back in one click.
/// </summary>
public class FormsModel(FormSchemaService forms) : PageModel
{
    public IReadOnlyList<FormSchema> VisitorForms { get; private set; } = [];
    public IReadOnlyList<FormSchema> ExhibitorForms { get; private set; } = [];
    public string? Message { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Message = TempData["message"] as string;
        VisitorForms = await forms.ListAsync(FormEntityKind.Visitor, ct);
        ExhibitorForms = await forms.ListAsync(FormEntityKind.Exhibitor, ct);
    }

    public async Task<IActionResult> OnPostActivateAsync(int id, CancellationToken ct)
    {
        await forms.ActivateAsync(id, User.Identity?.Name, ct);
        TempData["message"] = "That version is now the live form.";
        return RedirectToPage();
    }

    /// <summary>Put the built-in layout back, as a new version, without touching the current one.</summary>
    public async Task<IActionResult> OnPostResetAsync(FormEntityKind entity, CancellationToken ct)
    {
        var definition = entity == FormEntityKind.Visitor ? FormDefaults.Visitor() : FormDefaults.Exhibitor();
        var (saved, problems, _) = await forms.SaveAsync(definition, User.Identity?.Name, activate: true, ct);

        TempData["message"] = saved
            ? $"The built-in {entity.ToString().ToLowerInvariant()} form has been restored as a new version."
            : string.Join(" ", problems);

        return RedirectToPage();
    }
}
