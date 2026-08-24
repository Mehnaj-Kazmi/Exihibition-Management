namespace Exb.Core.Forms;

/// <summary>
/// The layouts a new installation starts with. They are a starting point, not a
/// fixed structure: every field here can be relabelled, reordered, moved between
/// sections, disabled or deleted from Settings &gt; Forms, and new ones added.
/// </summary>
public static class FormDefaults
{
    public static FormDefinition Visitor() => new()
    {
        Entity = FormEntityKind.Visitor,
        Name = "Visitor registration",
        Description = "Completed at the registration desk when the badge is issued.",
        Sections =
        [
            new FormSection
            {
                Id = "identity",
                Title = "Visitor",
                Columns = 2,
                Fields =
                [
                    Core("fullName", "Full name", "FullName", FormFieldType.Text, required: true, width: 2),
                    Core("email", "Email address", "Email", FormFieldType.Email, required: true,
                        help: "Your e-catalogues and daily summary are sent here."),
                    Core("phone", "Mobile", "Phone", FormFieldType.Phone),
                    Core("company", "Company", "Company", FormFieldType.Text),
                    Core("jobTitle", "Job title", "JobTitle", FormFieldType.Text),
                    Core("country", "Country", "Country", FormFieldType.Country),
                ],
            },

            new FormSection
            {
                Id = "profile",
                Title = "Why you are visiting",
                Description = "Used to suggest stands you would otherwise miss.",
                Columns = 2,
                Fields =
                [
                    new FormField
                    {
                        Key = "visitorType",
                        Label = "You are visiting as",
                        Type = FormFieldType.Select,
                        Required = true,
                        Options =
                        [
                            new() { Value = "buyer", Label = "Buyer / procurement" },
                            new() { Value = "distributor", Label = "Distributor / agent" },
                            new() { Value = "manufacturer", Label = "Manufacturer" },
                            new() { Value = "consultant", Label = "Consultant" },
                            new() { Value = "press", Label = "Press" },
                            new() { Value = "student", Label = "Student" },
                            new() { Value = "other", Label = "Other" },
                        ],
                    },
                    new FormField
                    {
                        Key = "purchasingRole",
                        Label = "Your role in purchasing",
                        Type = FormFieldType.Select,
                        Options =
                        [
                            new() { Value = "decision", Label = "I decide" },
                            new() { Value = "influence", Label = "I influence the decision" },
                            new() { Value = "research", Label = "I am researching" },
                            new() { Value = "none", Label = "Not involved" },
                        ],
                    },
                    new FormField
                    {
                        Key = "interests",
                        Label = "Product areas of interest",
                        Type = FormFieldType.MultiSelect,
                        Width = 2,
                        HelpText = "Optional. Tracking will find the rest on its own.",
                        // Filled from the live category list, so adding a category
                        // does not mean re-editing this form.
                        OptionsSource = FormOptionSources.Categories,
                    },
                    new FormField
                    {
                        Key = "budgetWindow",
                        Label = "Buying timeframe",
                        Type = FormFieldType.Radio,
                        Options =
                        [
                            new() { Value = "now", Label = "Now" },
                            new() { Value = "6m", Label = "Within 6 months" },
                            new() { Value = "12m", Label = "Within 12 months" },
                            new() { Value = "browsing", Label = "Just looking" },
                        ],
                    },
                ],
            },

            new FormSection
            {
                Id = "badge",
                Title = "Badge and consent",
                Columns = 2,
                Fields =
                [
                    Core("badgeEpc", "Badge tag (EPC)", "BadgeEpc", FormFieldType.Text, required: true,
                        help: "Scanned from the badge inlay at the desk.", adminOnly: true),
                    Core("language", "Report language", "Language", FormFieldType.Select),
                    Core("consentTracking", "I agree to my badge being tracked on the exhibition floor",
                        "ConsentTracking", FormFieldType.Checkbox, width: 2,
                        help: "Without this we record no stand visits and send no interest report."),
                    Core("consentEmail", "Send me my e-catalogues and daily summary by email",
                        "ConsentEmail", FormFieldType.Checkbox, width: 2),
                ],
            },
        ],
    };

    public static FormDefinition Exhibitor() => new()
    {
        Entity = FormEntityKind.Exhibitor,
        Name = "Exhibitor profile",
        Description = "Completed by the organiser or by the exhibitor before the show opens.",
        Sections =
        [
            new FormSection
            {
                Id = "company",
                Title = "Company",
                Columns = 2,
                Fields =
                [
                    Core("companyName", "Company name", "CompanyName", FormFieldType.Text, required: true, width: 2),
                    Core("summary", "Short description", "Summary", FormFieldType.TextArea, width: 2,
                        help: "One or two lines. This appears in visitors' daily reports."),
                    Core("website", "Website", "Website", FormFieldType.Url),
                    Core("country", "Country", "Country", FormFieldType.Country),
                ],
            },

            new FormSection
            {
                Id = "classification",
                Title = "Classification",
                Description = "Drives which visitors are told about this stand in their evening report.",
                Columns = 2,
                Fields =
                [
                    Core("categoryId", "Category", "CategoryId", FormFieldType.Select, required: true),
                    Core("subCategoryId", "Sub-category", "SubCategoryId", FormFieldType.Select),
                    new FormField
                    {
                        Key = "productKeywords",
                        Label = "Product keywords",
                        Type = FormFieldType.Text,
                        Width = 2,
                        HelpText = "Comma separated. Shown in the exhibitor directory.",
                        MaxLength = 400,
                    },
                    new FormField
                    {
                        Key = "brandsRepresented",
                        Label = "Brands represented",
                        Type = FormFieldType.Text,
                        Width = 2,
                        MaxLength = 400,
                    },
                ],
            },

            new FormSection
            {
                Id = "contact",
                Title = "Stand contact",
                Columns = 2,
                Fields =
                [
                    Core("contactName", "Contact name", "ContactName", FormFieldType.Text),
                    Core("email", "Email", "Email", FormFieldType.Email,
                        help: "Where the daily lead report for this stand is sent."),
                    Core("phone", "Phone", "Phone", FormFieldType.Phone),
                    new FormField
                    {
                        Key = "standStaffCount",
                        Label = "Stand staff",
                        Type = FormFieldType.Number,
                        Min = 0,
                        Max = 200,
                    },
                ],
            },
        ],
    };

    private static FormField Core(
        string key,
        string label,
        string property,
        FormFieldType type,
        bool required = false,
        int width = 1,
        string? help = null,
        bool adminOnly = false)
        => new()
        {
            Key = key,
            Label = label,
            Type = type,
            CoreProperty = property,
            Required = required,
            Width = width,
            HelpText = help,
            AdminOnly = adminOnly,
        };
}
