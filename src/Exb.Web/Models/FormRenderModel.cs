using Exb.Core.Forms;

namespace Exb.Web.Models;

public sealed record RenderOption(string Value, string Label);

/// <summary>
/// Everything the dynamic form partial needs to draw an admin-arranged form.
///
/// Options for the category and language fields are supplied here rather than
/// stored in the schema, because they come from live data: an organiser adding a
/// category should not have to go and re-edit the exhibitor form afterwards.
/// </summary>
public sealed class FormRenderModel
{
    public required FormDefinition Form { get; init; }
    public Dictionary<string, string[]> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Errors { get; init; } = new Dictionary<string, string>();

    /// <summary>True on staff-facing pages, which is what reveals admin-only fields.</summary>
    public bool AdminContext { get; init; } = true;

    /// <summary>Options for fields bound to a core property, keyed by that property.</summary>
    public Dictionary<string, IReadOnlyList<RenderOption>> DynamicOptions { get; init; } = [];

    /// <summary>Live lists a field can draw its choices from, keyed by <see cref="FormOptionSources"/> name.</summary>
    public Dictionary<string, IReadOnlyList<RenderOption>> SourceOptions { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string[] ValueFor(string key) => Values.TryGetValue(key, out var v) ? v : [];

    public string SingleValue(string key)
    {
        var values = ValueFor(key);
        return values.Length > 0 ? values[0] ?? "" : "";
    }

    public bool IsChecked(string key)
    {
        string value = SingleValue(key);
        return value is "true" or "on" or "1";
    }

    public string? ErrorFor(string key) => Errors.TryGetValue(key, out string? message) ? message : null;

    public IReadOnlyList<RenderOption> OptionsFor(FormField field)
    {
        if (field.OptionsSource is not null)
        {
            if (SourceOptions.TryGetValue(field.OptionsSource, out var live)) return live;
            if (field.OptionsSource.Equals(FormOptionSources.Languages, StringComparison.OrdinalIgnoreCase))
                return FormOptions.Languages;
            if (field.OptionsSource.Equals(FormOptionSources.Countries, StringComparison.OrdinalIgnoreCase))
                return FormOptions.Countries;
            return [];
        }

        if (field.IsCore && DynamicOptions.TryGetValue(field.CoreProperty!, out var options))
            return options;

        return field.Options.Select(o => new RenderOption(o.Value, o.Label)).ToList();
    }

    /// <summary>Fields the current audience should actually see.</summary>
    public IEnumerable<FormField> VisibleFields(FormSection section)
        => section.Fields.Where(f => f.Enabled && (AdminContext || !f.AdminOnly));
}
