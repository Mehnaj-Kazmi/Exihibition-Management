using System.Text.Json;
using System.Text.Json.Serialization;

namespace Exb.Core.Forms;

public enum FormFieldType
{
    Text = 0,
    TextArea = 1,
    Email = 2,
    Phone = 3,
    Number = 4,
    Date = 5,
    Select = 6,
    MultiSelect = 7,
    Checkbox = 8,
    Radio = 9,
    Url = 10,
    Country = 11,

    /// <summary>Layout only: a sub-heading inside a section.</summary>
    Heading = 12,

    /// <summary>Layout only: a horizontal rule.</summary>
    Divider = 13,
}

public sealed class FormOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>Lists a field can draw its choices from at render time.</summary>
public static class FormOptionSources
{
    public const string Categories = "categories";
    public const string SubCategories = "subcategories";
    public const string Languages = "languages";
    public const string Countries = "countries";

    public static readonly string[] All = [Categories, SubCategories, Languages, Countries];

    public static bool IsKnown(string? source)
        => source is not null && All.Contains(source, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One question on a form.
///
/// <see cref="CoreProperty"/> is what keeps the modularity honest. Most fields
/// are just questions the organiser wants answered this year, and their answers
/// live in the entity's JSON profile. But a handful of fields — the visitor's
/// email, their badge EPC, the exhibitor's category — are things the system
/// itself acts on, and those are bound to real columns. An admin can move them,
/// relabel them and reorder them freely; what they cannot do is delete the email
/// field and then wonder why the evening pack never arrived, because the form
/// validator refuses to save a layout that has dropped a required core field.
/// </summary>
public sealed class FormField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public FormFieldType Type { get; set; } = FormFieldType.Text;

    public bool Required { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Shown on the admin form but not on the public registration form.</summary>
    public bool AdminOnly { get; set; }

    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public string? DefaultValue { get; set; }

    public List<FormOption> Options { get; set; } = [];

    /// <summary>
    /// Fill the choices from live data instead of from <see cref="Options"/>.
    /// One of <see cref="FormOptionSources"/>.
    ///
    /// This exists because the useful lists are the ones that change: an
    /// organiser who adds a product category should not then have to remember to
    /// go and re-edit the registration form's "areas of interest" question. A
    /// field with a source stays in step on its own.
    /// </summary>
    public string? OptionsSource { get; set; }

    /// <summary>Columns this field spans within its section, 1 or 2.</summary>
    public int Width { get; set; } = 1;

    /// <summary>Entity property this field writes to, or null to store it in the JSON profile.</summary>
    public string? CoreProperty { get; set; }

    public int? MaxLength { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }

    /// <summary>Regular expression the answer must match, for things like a VAT number.</summary>
    public string? Pattern { get; set; }
    public string? PatternMessage { get; set; }

    [JsonIgnore]
    public bool IsLayoutOnly => Type is FormFieldType.Heading or FormFieldType.Divider;

    [JsonIgnore]
    public bool IsCore => !string.IsNullOrWhiteSpace(CoreProperty);

    public FormField Clone() => JsonSerializer.Deserialize<FormField>(JsonSerializer.Serialize(this))!;
}

public sealed class FormSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>1 or 2 columns. Registration desks work faster on two.</summary>
    public int Columns { get; set; } = 2;

    public bool Enabled { get; set; } = true;
    public List<FormField> Fields { get; set; } = [];
}

public enum FormEntityKind
{
    Visitor = 0,
    Exhibitor = 1,
}

/// <summary>
/// A whole form layout, as arranged by the admin for one exhibition.
/// Serialised to the SchemaJson column of a FormSchema row.
/// </summary>
public sealed class FormDefinition
{
    public FormEntityKind Entity { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<FormSection> Sections { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static FormDefinition FromJson(string json)
        => JsonSerializer.Deserialize<FormDefinition>(json, Json)
           ?? throw new InvalidOperationException("form schema JSON could not be read");

    public FormDefinition Clone() => FromJson(ToJson());

    public IEnumerable<FormField> AllFields => Sections.SelectMany(s => s.Fields);

    public IEnumerable<FormField> ActiveFields =>
        Sections.Where(s => s.Enabled).SelectMany(s => s.Fields).Where(f => f.Enabled && !f.IsLayoutOnly);

    public FormField? Field(string key)
        => AllFields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    public FormSection? SectionOf(string fieldKey)
        => Sections.FirstOrDefault(s => s.Fields.Any(f => string.Equals(f.Key, fieldKey, StringComparison.OrdinalIgnoreCase)));

    // --- rearrangement, which is the entire point of this class ---------------

    public bool MoveField(string fieldKey, int delta)
    {
        var section = SectionOf(fieldKey);
        if (section is null) return false;

        int index = section.Fields.FindIndex(f => f.Key == fieldKey);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= section.Fields.Count) return false;

        (section.Fields[index], section.Fields[target]) = (section.Fields[target], section.Fields[index]);
        return true;
    }

    public bool MoveFieldToSection(string fieldKey, string sectionId, int position = -1)
    {
        var from = SectionOf(fieldKey);
        var to = Sections.FirstOrDefault(s => s.Id == sectionId);
        if (from is null || to is null) return false;

        var field = from.Fields.First(f => f.Key == fieldKey);
        from.Fields.Remove(field);

        if (position < 0 || position > to.Fields.Count) to.Fields.Add(field);
        else to.Fields.Insert(position, field);
        return true;
    }

    public bool MoveSection(string sectionId, int delta)
    {
        int index = Sections.FindIndex(s => s.Id == sectionId);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Sections.Count) return false;

        (Sections[index], Sections[target]) = (Sections[target], Sections[index]);
        return true;
    }

    /// <summary>Reorder a section's fields to match an explicit list of keys, for drag and drop.</summary>
    public bool ReorderSection(string sectionId, IReadOnlyList<string> keyOrder)
    {
        var section = Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null) return false;

        var byKey = section.Fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        var reordered = new List<FormField>(section.Fields.Count);

        foreach (string key in keyOrder)
            if (byKey.Remove(key, out var field)) reordered.Add(field);

        // Anything the client did not mention keeps its place at the end, so a
        // stale browser tab cannot silently drop a field.
        reordered.AddRange(section.Fields.Where(f => byKey.ContainsKey(f.Key)));

        section.Fields = reordered;
        return true;
    }

    public bool RemoveField(string fieldKey)
    {
        var section = SectionOf(fieldKey);
        if (section is null) return false;
        return section.Fields.RemoveAll(f => f.Key == fieldKey) > 0;
    }

    public void AddField(string sectionId, FormField field)
    {
        var section = Sections.FirstOrDefault(s => s.Id == sectionId)
            ?? throw new ArgumentException($"no section '{sectionId}'", nameof(sectionId));
        section.Fields.Add(field);
    }
}
