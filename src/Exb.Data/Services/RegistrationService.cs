using System.Globalization;
using Exb.Core.Forms;
using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data.Services;

public sealed record SaveOutcome(bool Saved, int? Id, IReadOnlyDictionary<string, string> Errors)
{
    public static SaveOutcome Failed(string field, string message) =>
        new(false, null, new Dictionary<string, string> { [field] = message });
}

/// <summary>
/// Creates and updates visitors and exhibitors from whatever shape the admin has
/// arranged their forms into.
///
/// The binding from a form answer to a database column is an explicit switch
/// rather than reflection. Reflection would be shorter, but these forms are
/// admin-editable at runtime, and a mistyped property name would then become a
/// silent write to whatever field happened to match. Being explicit means an
/// unknown binding is a visible error instead.
/// </summary>
public sealed class RegistrationService(
    IDbContextFactory<ExhibitionDbContext> factory,
    BadgeDirectory badges)
{
    // --- visitors ------------------------------------------------------------

    public async Task<SaveOutcome> SaveVisitorAsync(
        int? visitorId,
        FormSubmissionResult submission,
        string? user,
        CancellationToken ct = default)
    {
        if (!submission.IsValid) return new SaveOutcome(false, visitorId, submission.Errors);

        await using var db = await factory.CreateDbContextAsync(ct);

        var visitor = visitorId is null
            ? new Visitor
            {
                RegistrationCode = Tokens.RegistrationCode(),
                AccessToken = Tokens.New(24),
            }
            : await db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId, ct)
              ?? throw new InvalidOperationException($"no visitor {visitorId}");

        string? previousEpc = visitor.BadgeEpc;

        foreach (var (property, value) in submission.CoreValues)
        {
            switch (property)
            {
                case "FullName": visitor.FullName = Text(value) ?? ""; break;
                case "Email": visitor.Email = Text(value) ?? ""; break;
                case "Phone": visitor.Phone = Text(value); break;
                case "Company": visitor.Company = Text(value); break;
                case "JobTitle": visitor.JobTitle = Text(value); break;
                case "Country": visitor.Country = Text(value); break;
                case "Language": visitor.Language = Text(value); break;
                case "BadgeEpc": visitor.BadgeEpc = NormaliseEpc(Text(value)); break;
                case "ConsentEmail": visitor.ConsentEmail = Flag(value); break;
                case "ConsentTracking": visitor.ConsentTracking = Flag(value); break;
                default:
                    throw new InvalidOperationException(
                        $"Visitor form is bound to unknown column '{property}'. Fix it in Settings > Forms.");
            }
        }

        visitor.ProfileJson = submission.ProfileJson;
        visitor.UpdatedUtc = DateTime.UtcNow;

        // The badge EPC is unique across the show; catching it here gives the
        // desk a usable message instead of a database exception.
        if (!string.IsNullOrEmpty(visitor.BadgeEpc))
        {
            bool clash = await db.Visitors.AnyAsync(
                v => v.BadgeEpc == visitor.BadgeEpc && v.Id != visitor.Id, ct);
            if (clash)
                return SaveOutcome.Failed("badgeEpc", "That badge is already issued to another visitor.");
        }

        if (visitorId is null) db.Visitors.Add(visitor);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = visitorId is null ? "visitor.create" : "visitor.update",
            EntityName = "Visitor",
            EntityId = visitor.Id.ToString(),
            User = user,
        });

        await db.SaveChangesAsync(ct);

        // Make the badge trackable immediately rather than at the next refresh.
        if (!string.IsNullOrEmpty(previousEpc) && previousEpc != visitor.BadgeEpc) badges.Remove(previousEpc);
        if (!string.IsNullOrEmpty(visitor.BadgeEpc))
            badges.Upsert(visitor.BadgeEpc, visitor.Id, visitor.ConsentTracking);

        return new SaveOutcome(true, visitor.Id, new Dictionary<string, string>());
    }

    // --- exhibitors ----------------------------------------------------------

    public async Task<SaveOutcome> SaveExhibitorAsync(
        int? exhibitorId,
        FormSubmissionResult submission,
        string? user,
        CancellationToken ct = default)
    {
        if (!submission.IsValid) return new SaveOutcome(false, exhibitorId, submission.Errors);

        await using var db = await factory.CreateDbContextAsync(ct);

        var exhibitor = exhibitorId is null
            ? new Exhibitor { Code = Tokens.New(8) }
            : await db.Exhibitors.FirstOrDefaultAsync(e => e.Id == exhibitorId, ct)
              ?? throw new InvalidOperationException($"no exhibitor {exhibitorId}");

        foreach (var (property, value) in submission.CoreValues)
        {
            switch (property)
            {
                case "CompanyName": exhibitor.CompanyName = Text(value) ?? ""; break;
                case "ContactName": exhibitor.ContactName = Text(value); break;
                case "Email": exhibitor.Email = Text(value); break;
                case "Phone": exhibitor.Phone = Text(value); break;
                case "Website": exhibitor.Website = Text(value); break;
                case "Country": exhibitor.Country = Text(value); break;
                case "Summary": exhibitor.Summary = Text(value); break;
                case "CategoryId": exhibitor.CategoryId = Number(value); break;
                case "SubCategoryId": exhibitor.SubCategoryId = Number(value); break;
                default:
                    throw new InvalidOperationException(
                        $"Exhibitor form is bound to unknown column '{property}'. Fix it in Settings > Forms.");
            }
        }

        // A sub-category that does not belong to the chosen category would
        // quietly poison the missed-stand matching, so it is rejected rather
        // than stored.
        if (exhibitor.SubCategoryId is not null)
        {
            var parentId = await db.Categories
                .Where(c => c.Id == exhibitor.SubCategoryId)
                .Select(c => c.ParentId)
                .FirstOrDefaultAsync(ct);

            if (parentId != exhibitor.CategoryId)
                return SaveOutcome.Failed("subCategoryId", "That sub-category does not belong to the selected category.");
        }

        exhibitor.ProfileJson = submission.ProfileJson;
        exhibitor.UpdatedUtc = DateTime.UtcNow;

        if (exhibitorId is null) db.Exhibitors.Add(exhibitor);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = exhibitorId is null ? "exhibitor.create" : "exhibitor.update",
            EntityName = "Exhibitor",
            EntityId = exhibitor.Id.ToString(),
            User = user,
        });

        await db.SaveChangesAsync(ct);
        return new SaveOutcome(true, exhibitor.Id, new Dictionary<string, string>());
    }

    /// <summary>Current answers, flattened so the form renderer can repopulate the fields.</summary>
    public static Dictionary<string, string[]> ToFormValues(Visitor v, FormDefinition form)
    {
        var values = ProfileValues(v.ProfileJson);
        foreach (var field in form.ActiveFields.Where(f => f.IsCore))
        {
            values[field.Key] = field.CoreProperty switch
            {
                "FullName" => [v.FullName],
                "Email" => [v.Email],
                "Phone" => [v.Phone ?? ""],
                "Company" => [v.Company ?? ""],
                "JobTitle" => [v.JobTitle ?? ""],
                "Country" => [v.Country ?? ""],
                "Language" => [v.Language ?? ""],
                "BadgeEpc" => [v.BadgeEpc],
                "ConsentEmail" => [v.ConsentEmail ? "true" : ""],
                "ConsentTracking" => [v.ConsentTracking ? "true" : ""],
                _ => [""],
            };
        }
        return values;
    }

    public static Dictionary<string, string[]> ToFormValues(Exhibitor e, FormDefinition form)
    {
        var values = ProfileValues(e.ProfileJson);
        foreach (var field in form.ActiveFields.Where(f => f.IsCore))
        {
            values[field.Key] = field.CoreProperty switch
            {
                "CompanyName" => [e.CompanyName],
                "ContactName" => [e.ContactName ?? ""],
                "Email" => [e.Email ?? ""],
                "Phone" => [e.Phone ?? ""],
                "Website" => [e.Website ?? ""],
                "Country" => [e.Country ?? ""],
                "Summary" => [e.Summary ?? ""],
                "CategoryId" => [e.CategoryId?.ToString() ?? ""],
                "SubCategoryId" => [e.SubCategoryId?.ToString() ?? ""],
                _ => [""],
            };
        }
        return values;
    }

    private static Dictionary<string, string[]> ProfileValues(string profileJson)
    {
        var values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(profileJson)) return values;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(profileJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Array =>
                        property.Value.EnumerateArray().Select(e => e.ToString()).ToArray(),
                    System.Text.Json.JsonValueKind.True => ["true"],
                    System.Text.Json.JsonValueKind.False => [""],
                    System.Text.Json.JsonValueKind.Null => [""],
                    _ => [property.Value.ToString()],
                };
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // An unreadable profile just means the extra answers cannot be shown.
        }

        return values;
    }

    /// <summary>EPCs are hex and case-insensitive; store them one way so lookups match.</summary>
    private static string NormaliseEpc(string? value)
        => (value ?? "").Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();

    private static string? Text(object? value)
        => value switch
        {
            null => null,
            string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
            string[] a => a.Length == 0 ? null : string.Join(", ", a),
            double d => d.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => value.ToString(),
        };

    private static bool Flag(object? value) => value is bool b ? b : Text(value) is "true" or "on" or "1";

    private static int? Number(object? value)
        => value switch
        {
            null => null,
            double d => (int)d,
            string s when int.TryParse(s, out int n) => n,
            _ => null,
        };
}
