namespace Exb.Core.Forms;

/// <summary>
/// A form field that is wired to a real database column instead of the JSON profile.
/// </summary>
/// <param name="SystemRequired">
/// True when the product stops working without it. The layout editor will not let
/// an admin save a form that has dropped one of these.
/// </param>
/// <param name="Why">Shown in the layout editor, so the restriction is explained rather than just enforced.</param>
public sealed record CoreBinding(
    string Property,
    string Label,
    FormFieldType Type,
    bool SystemRequired,
    string Why);

public static class CoreProperties
{
    private static readonly CoreBinding[] VisitorBindings =
    [
        new("FullName", "Full name", FormFieldType.Text, true,
            "Named on the badge and on the daily report."),
        new("Email", "Email address", FormFieldType.Email, true,
            "Where the e-catalogue pack and the daily report are sent."),
        new("BadgeEpc", "Badge tag (EPC)", FormFieldType.Text, true,
            "Links the RFID badge to this visitor. Without it nothing can be tracked."),
        new("Phone", "Phone", FormFieldType.Phone, false, "Contact number."),
        new("Company", "Company", FormFieldType.Text, false, "Shown to exhibitors on lead reports."),
        new("JobTitle", "Job title", FormFieldType.Text, false, "Shown to exhibitors on lead reports."),
        new("Country", "Country", FormFieldType.Country, false, "Used for visitor demographics."),
        new("Language", "Preferred language", FormFieldType.Select, false, "Language of the daily report."),
        new("ConsentEmail", "Consent to email", FormFieldType.Checkbox, false,
            "No pack or report is sent without this."),
        new("ConsentTracking", "Consent to visit tracking", FormFieldType.Checkbox, false,
            "No dwell time is recorded without this."),
    ];

    private static readonly CoreBinding[] ExhibitorBindings =
    [
        new("CompanyName", "Company name", FormFieldType.Text, true,
            "Named on the stand, in the pack and in every report."),
        new("CategoryId", "Category", FormFieldType.Select, true,
            "Drives the interest rollup and which visitors are told about this stand."),
        new("SubCategoryId", "Sub-category", FormFieldType.Select, false,
            "Sharpens the missed-stand recommendations."),
        new("ContactName", "Contact name", FormFieldType.Text, false, "Stand contact."),
        new("Email", "Email", FormFieldType.Email, false, "Where lead reports are sent."),
        new("Phone", "Phone", FormFieldType.Phone, false, "Stand contact number."),
        new("Website", "Website", FormFieldType.Url, false, "Linked from the visitor's daily report."),
        new("Country", "Country", FormFieldType.Country, false, "Shown in the exhibitor directory."),
        new("Summary", "Short description", FormFieldType.TextArea, false,
            "One or two lines, shown in the missed-stand table."),
    ];

    public static IReadOnlyList<CoreBinding> For(FormEntityKind entity)
        => entity == FormEntityKind.Visitor ? VisitorBindings : ExhibitorBindings;

    public static CoreBinding? Find(FormEntityKind entity, string? property)
        => property is null
            ? null
            : For(entity).FirstOrDefault(b => string.Equals(b.Property, property, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<CoreBinding> SystemRequired(FormEntityKind entity)
        => For(entity).Where(b => b.SystemRequired);
}
