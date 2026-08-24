using Exb.Core.Forms;
using Exb.Data.Services;
using Exb.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// The form layout editor.
///
/// The working copy is carried in a hidden field as JSON rather than held in
/// session. That keeps the editor completely stateless — two organisers can have
/// it open at once, a browser refresh cannot resurrect a half-finished edit, and
/// nothing is written until Save is pressed, at which point the layout is
/// validated as a whole.
/// </summary>
public class FormEditModel(FormSchemaService forms) : PageModel
{
    [BindProperty(SupportsGet = true)] public FormEntityKind Entity { get; set; } = FormEntityKind.Visitor;
    [BindProperty(SupportsGet = true)] public int? SchemaId { get; set; }

    [BindProperty] public string DefinitionJson { get; set; } = "";

    public FormDefinition Definition { get; private set; } = new();
    public FormRenderModel Preview { get; private set; } = null!;
    public IReadOnlyList<CoreBinding> Bindings => CoreProperties.For(Entity);
    public IReadOnlyList<string> Problems { get; private set; } = [];
    public string? Message { get; private set; }
    public bool Dirty { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Message = TempData["message"] as string;

        if (SchemaId is not null && await forms.GetAsync(SchemaId.Value, ct) is { } stored)
        {
            Definition = FormDefinition.FromJson(stored.SchemaJson);
            Entity = (FormEntityKind)stored.Entity;
        }
        else
        {
            Definition = await forms.GetActiveAsync(Entity, ct);
        }

        Finish();
    }

    // --- rearrangement handlers ---------------------------------------------

    public IActionResult OnPostMoveField(string key, int delta)
        => Mutate(d => d.MoveField(key, delta));

    public IActionResult OnPostMoveSection(string sectionId, int delta)
        => Mutate(d => d.MoveSection(sectionId, delta));

    public IActionResult OnPostMoveToSection(string key, string sectionId)
        => Mutate(d => d.MoveFieldToSection(key, sectionId));

    public IActionResult OnPostRemoveField(string key)
        => Mutate(d => d.RemoveField(key));

    public IActionResult OnPostToggleField(string key)
        => Mutate(d =>
        {
            var field = d.Field(key);
            if (field is null) return false;
            field.Enabled = !field.Enabled;
            return true;
        });

    public IActionResult OnPostToggleRequired(string key)
        => Mutate(d =>
        {
            var field = d.Field(key);
            if (field is null) return false;
            field.Required = !field.Required;
            return true;
        });

    public IActionResult OnPostUpdateField(
        string key, string label, string? helpText, int width, bool adminOnly,
        string? optionsText, int? maxLength, string? pattern, string? patternMessage)
        => Mutate(d =>
        {
            var field = d.Field(key);
            if (field is null) return false;

            field.Label = (label ?? "").Trim();
            field.HelpText = string.IsNullOrWhiteSpace(helpText) ? null : helpText.Trim();
            field.Width = width >= 2 ? 2 : 1;
            field.AdminOnly = adminOnly;
            field.MaxLength = maxLength is > 0 ? maxLength : null;
            field.Pattern = string.IsNullOrWhiteSpace(pattern) ? null : pattern.Trim();
            field.PatternMessage = string.IsNullOrWhiteSpace(patternMessage) ? null : patternMessage.Trim();

            if (optionsText is not null) field.Options = ParseOptions(optionsText);
            return true;
        });

    public IActionResult OnPostAddField(
        string sectionId, string key, string label, FormFieldType type,
        string? coreProperty, bool required, int width, string? optionsText, string? optionsSource)
        => Mutate(d =>
        {
            key = Slug(key, label);
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (d.Field(key) is not null) return false;   // ValidateLayout reports the duplicate

            d.AddField(sectionId, new FormField
            {
                Key = key,
                Label = (label ?? "").Trim(),
                Type = type,
                CoreProperty = string.IsNullOrWhiteSpace(coreProperty) ? null : coreProperty,
                OptionsSource = string.IsNullOrWhiteSpace(optionsSource) ? null : optionsSource,
                Required = required,
                Width = width >= 2 ? 2 : 1,
                Options = optionsText is null ? [] : ParseOptions(optionsText),
            });
            return true;
        });

    public IActionResult OnPostAddSection(string title, int columns)
        => Mutate(d =>
        {
            d.Sections.Add(new FormSection
            {
                Title = string.IsNullOrWhiteSpace(title) ? "New section" : title.Trim(),
                Columns = columns >= 2 ? 2 : 1,
            });
            return true;
        });

    public IActionResult OnPostUpdateSection(string sectionId, string title, string? description, int columns)
        => Mutate(d =>
        {
            var section = d.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section is null) return false;

            section.Title = (title ?? "").Trim();
            section.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            section.Columns = columns >= 2 ? 2 : 1;
            return true;
        });

    public IActionResult OnPostRemoveSection(string sectionId)
        => Mutate(d =>
        {
            var section = d.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section is null) return false;

            // Fields are moved rather than destroyed, so removing a section by
            // mistake cannot quietly delete the email field with it.
            var target = d.Sections.FirstOrDefault(s => s.Id != sectionId);
            if (target is not null) target.Fields.AddRange(section.Fields);

            d.Sections.Remove(section);
            return true;
        });

    // --- saving --------------------------------------------------------------

    public async Task<IActionResult> OnPostSaveAsync(string name, CancellationToken ct)
    {
        Definition = Read();
        Definition.Entity = Entity;
        if (!string.IsNullOrWhiteSpace(name)) Definition.Name = name.Trim();

        var (saved, problems, _) = await forms.SaveAsync(Definition, User.Identity?.Name, activate: true, ct);

        if (!saved)
        {
            Problems = problems;
            Dirty = true;
            Finish();
            return Page();
        }

        TempData["message"] = $"Saved as a new version and made live. Registration will use it immediately.";
        return RedirectToPage("/Settings/Forms");
    }

    // --- plumbing ------------------------------------------------------------

    private IActionResult Mutate(Func<FormDefinition, bool> operation)
    {
        Definition = Read();
        operation(Definition);
        Dirty = true;
        Finish();
        return Page();
    }

    private FormDefinition Read()
    {
        try
        {
            var definition = FormDefinition.FromJson(DefinitionJson);
            definition.Entity = Entity;
            return definition;
        }
        catch (Exception)
        {
            // A mangled hidden field must not lose the whole layout; fall back
            // to the built-in one and let the editor carry on.
            return Entity == FormEntityKind.Visitor ? FormDefaults.Visitor() : FormDefaults.Exhibitor();
        }
    }

    private void Finish()
    {
        DefinitionJson = Definition.ToJson();

        Preview = new FormRenderModel
        {
            Form = Definition,
            AdminContext = true,
            DynamicOptions =
            {
                ["Language"] = FormOptions.Languages,
                ["CategoryId"] = [new RenderOption("1", "— your categories appear here —")],
                ["SubCategoryId"] = [new RenderOption("1", "— your sub-categories appear here —")],
            },
            SourceOptions =
            {
                [FormOptionSources.Categories] = [new RenderOption("1", "— your categories appear here —")],
                [FormOptionSources.SubCategories] = [new RenderOption("1", "— your sub-categories appear here —")],
                [FormOptionSources.Languages] = FormOptions.Languages,
                [FormOptionSources.Countries] = FormOptions.Countries,
            },
        };

        if (Problems.Count == 0) Problems = FormValidator.ValidateLayout(Definition);
    }

    private static List<FormOption> ParseOptions(string text)
    {
        var options = new List<FormOption>();
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            // "value|Label", or just a label whose value is derived from it.
            int pipe = trimmed.IndexOf('|');
            if (pipe > 0)
                options.Add(new FormOption { Value = trimmed[..pipe].Trim(), Label = trimmed[(pipe + 1)..].Trim() });
            else
                options.Add(new FormOption { Value = Slug(trimmed, trimmed), Label = trimmed });
        }
        return options;
    }

    /// <summary>A stable, safe key. Field keys become HTML input names and JSON property names.</summary>
    private static string Slug(string? key, string? fallback)
    {
        string source = string.IsNullOrWhiteSpace(key) ? fallback ?? "" : key;
        var chars = source.Trim()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_')
            .ToArray();

        string cleaned = new(chars);
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";

        string result = words[0].ToLowerInvariant()
            + string.Concat(words.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

        return result.Length > 48 ? result[..48] : result;
    }
}
