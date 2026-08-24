namespace Exb.Web.Models;

/// <summary>Fixed option lists the form renderer supplies itself.</summary>
public static class FormOptions
{
    /// <summary>
    /// Countries offered on registration forms. Deliberately a short, editable
    /// list of the markets these shows actually draw from rather than the full
    /// ISO set: a registration desk queue is no place for a 249-entry dropdown.
    /// Add to it here, or replace the field with a plain text field in
    /// Settings &gt; Forms.
    /// </summary>
    public static readonly IReadOnlyList<RenderOption> Countries =
    [
        new("PK", "Pakistan"),
        new("AE", "United Arab Emirates"),
        new("SA", "Saudi Arabia"),
        new("QA", "Qatar"),
        new("OM", "Oman"),
        new("KW", "Kuwait"),
        new("BH", "Bahrain"),
        new("TR", "Türkiye"),
        new("EG", "Egypt"),
        new("IN", "India"),
        new("BD", "Bangladesh"),
        new("CN", "China"),
        new("MY", "Malaysia"),
        new("ID", "Indonesia"),
        new("DE", "Germany"),
        new("IT", "Italy"),
        new("ES", "Spain"),
        new("FR", "France"),
        new("NL", "Netherlands"),
        new("GB", "United Kingdom"),
        new("US", "United States"),
        new("OTHER", "Other"),
    ];

    public static readonly IReadOnlyList<RenderOption> Languages =
    [
        new("en", "English"),
        new("ar", "Arabic"),
        new("ur", "Urdu"),
        new("tr", "Turkish"),
        new("de", "German"),
        new("zh", "Chinese"),
    ];
}
