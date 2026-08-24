using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Exb.Core.Forms;

public sealed class FormSubmissionResult
{
    /// <summary>Values bound to real entity columns, keyed by property name.</summary>
    public Dictionary<string, object?> CoreValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Everything else, destined for the entity's JSON profile.</summary>
    public Dictionary<string, object?> ProfileValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Field key to message, for redisplaying the form.</summary>
    public Dictionary<string, string> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsValid => Errors.Count == 0;

    public string ProfileJson => JsonSerializer.Serialize(ProfileValues, new JsonSerializerOptions { WriteIndented = false });

    public T? Core<T>(string property)
        => CoreValues.TryGetValue(property, out var value) && value is not null ? (T)value : default;
}

/// <summary>
/// Validates a submitted form against the layout the admin arranged, and splits
/// the answers into real columns and JSON profile answers.
///
/// Validation is driven entirely by the schema, so an organiser who adds a
/// "VAT number, exactly 15 digits" field this year gets that enforced without a
/// deployment.
/// </summary>
public static class FormValidator
{
    private static readonly Regex EmailShape = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static FormSubmissionResult Validate(
        FormDefinition form,
        IReadOnlyDictionary<string, string[]> submitted,
        bool adminContext = false)
    {
        var result = new FormSubmissionResult();

        foreach (var section in form.Sections.Where(s => s.Enabled))
        {
            foreach (var field in section.Fields)
            {
                if (!field.Enabled || field.IsLayoutOnly) continue;
                if (field.AdminOnly && !adminContext) continue;

                string[] raw = submitted.TryGetValue(field.Key, out var values) ? values : [];
                object? value = Coerce(field, raw, result.Errors);

                if (result.Errors.ContainsKey(field.Key)) continue;

                if (IsEmpty(value))
                {
                    if (field.Required)
                    {
                        result.Errors[field.Key] = $"{field.Label} is required.";
                        continue;
                    }

                    if (!string.IsNullOrEmpty(field.DefaultValue))
                        value = Coerce(field, [field.DefaultValue], result.Errors);
                }

                if (!IsEmpty(value)) ApplyConstraints(field, value!, result.Errors);
                if (result.Errors.ContainsKey(field.Key)) continue;

                if (field.IsCore) result.CoreValues[field.CoreProperty!] = value;
                else result.ProfileValues[field.Key] = value;
            }
        }

        return result;
    }

    private static object? Coerce(FormField field, string[] raw, IDictionary<string, string> errors)
    {
        string first = raw.FirstOrDefault()?.Trim() ?? "";

        switch (field.Type)
        {
            case FormFieldType.Checkbox:
                // An unchecked box posts nothing at all, which is a definite false
                // rather than a missing answer.
                return raw.Length > 0 && first is not ("" or "false" or "off" or "0");

            case FormFieldType.MultiSelect:
                var chosen = raw.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
                // Only a fixed option list can be checked here. A field drawing
                // its choices from live data is validated by the foreign key it
                // ultimately lands on, not by a stale copy of the list.
                if (field.OptionsSource is null)
                    foreach (string v in chosen)
                        if (field.Options.Count > 0 && !field.Options.Any(o => o.Value == v))
                            errors[field.Key] = $"{field.Label}: '{v}' is not one of the options.";
                return chosen.Length == 0 ? null : chosen;

            case FormFieldType.Select:
            case FormFieldType.Radio:
                if (first.Length == 0) return null;
                if (field.OptionsSource is null && field.Options.Count > 0 && !field.Options.Any(o => o.Value == first))
                {
                    errors[field.Key] = $"{field.Label}: '{first}' is not one of the options.";
                    return null;
                }
                return first;

            case FormFieldType.Number:
                if (first.Length == 0) return null;
                if (!double.TryParse(first, NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
                {
                    errors[field.Key] = $"{field.Label} must be a number.";
                    return null;
                }
                return number;

            case FormFieldType.Date:
                if (first.Length == 0) return null;
                if (!DateOnly.TryParse(first, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    errors[field.Key] = $"{field.Label} must be a date.";
                    return null;
                }
                return date.ToString("yyyy-MM-dd");

            case FormFieldType.Email:
                if (first.Length == 0) return null;
                if (!EmailShape.IsMatch(first))
                {
                    errors[field.Key] = $"{field.Label} does not look like an email address.";
                    return null;
                }
                return first.ToLowerInvariant();

            default:
                return first.Length == 0 ? null : first;
        }
    }

    private static void ApplyConstraints(FormField field, object value, IDictionary<string, string> errors)
    {
        if (value is string text)
        {
            if (field.MaxLength is { } max && text.Length > max)
                errors[field.Key] = $"{field.Label} must be {max} characters or fewer.";

            if (!string.IsNullOrWhiteSpace(field.Pattern))
            {
                try
                {
                    // Timed out rather than trusted: the pattern comes from an
                    // admin form and a pathological one would otherwise hang the
                    // registration desk.
                    if (!Regex.IsMatch(text, field.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250)))
                        errors[field.Key] = field.PatternMessage ?? $"{field.Label} is not in the expected format.";
                }
                catch (RegexMatchTimeoutException)
                {
                    errors[field.Key] = $"{field.Label} could not be checked; simplify the validation pattern.";
                }
                catch (ArgumentException)
                {
                    errors[field.Key] = $"{field.Label} has an invalid validation pattern configured.";
                }
            }
        }

        if (value is double number)
        {
            if (field.Min is { } min && number < min)
                errors[field.Key] = $"{field.Label} must be at least {min}.";
            if (field.Max is { } max && number > max)
                errors[field.Key] = $"{field.Label} must be at most {max}.";
        }
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        string[] a => a.Length == 0,
        _ => false,
    };

    // --- layout validation, run when the admin saves a form ------------------

    /// <summary>
    /// Check a rearranged layout before it goes live. Catches the mistakes that
    /// would only show up as a silent failure hours later: a dropped email field
    /// meaning no packs go out, two fields sharing a key so one overwrites the
    /// other, or a select with nothing to select.
    /// </summary>
    public static IReadOnlyList<string> ValidateLayout(FormDefinition form)
    {
        var problems = new List<string>();

        var duplicates = form.AllFields
            .GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (string key in duplicates)
            problems.Add($"Field key '{key}' is used more than once. Keys must be unique.");

        foreach (var field in form.AllFields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
                problems.Add($"A field in '{form.SectionOf(field.Key)?.Title ?? "a section"}' has no key.");

            if (string.IsNullOrWhiteSpace(field.Label) && !field.IsLayoutOnly)
                problems.Add($"Field '{field.Key}' has no label.");

            bool suppliesItsOwnOptions = field.OptionsSource is not null
                || field.Type == FormFieldType.Country
                || field.CoreProperty is "CategoryId" or "SubCategoryId" or "Language";

            if (field.Type is FormFieldType.Select or FormFieldType.Radio or FormFieldType.MultiSelect
                && field.Options.Count == 0
                && !suppliesItsOwnOptions)
                problems.Add($"Field '{field.Label}' is a {field.Type} but has no options, so nothing can be chosen.");

            if (field.OptionsSource is not null && !FormOptionSources.IsKnown(field.OptionsSource))
                problems.Add($"Field '{field.Label}' draws its options from unknown source '{field.OptionsSource}'.");

            if (field.CoreProperty is not null && CoreProperties.Find(form.Entity, field.CoreProperty) is null)
                problems.Add($"Field '{field.Label}' is bound to unknown property '{field.CoreProperty}'.");
        }

        var boundProperties = form.ActiveFields
            .Where(f => f.IsCore)
            .Select(f => f.CoreProperty!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in CoreProperties.SystemRequired(form.Entity))
            if (!boundProperties.Contains(binding.Property))
                problems.Add($"'{binding.Label}' must be on the form and enabled. {binding.Why}");

        var duplicateBindings = form.ActiveFields
            .Where(f => f.IsCore)
            .GroupBy(f => f.CoreProperty!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var group in duplicateBindings)
            problems.Add($"Two fields are both bound to '{group.Key}'. Only one field may write to a column.");

        if (form.Sections.Count == 0)
            problems.Add("The form has no sections.");

        return problems;
    }
}
