using Exb.Core.Forms;
using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data.Services;

/// <summary>
/// Manages the admin-arranged visitor and exhibitor forms.
///
/// Layouts are versioned and only one per entity is live, enforced by a filtered
/// unique index in SQL Server rather than by application discipline. Saving an
/// edited layout creates a new version rather than overwriting: a registration
/// desk halfway through a morning must not have the form change under it, and
/// when an organiser breaks something at 09:00 on opening day, rolling back to
/// the previous version has to be one click.
/// </summary>
public sealed class FormSchemaService(IDbContextFactory<ExhibitionDbContext> factory)
{
    public async Task<FormDefinition> GetActiveAsync(FormEntityKind entity, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.FormSchemas
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Entity == (FormEntity)entity && f.IsActive, ct);

        if (row is not null)
        {
            try
            {
                return FormDefinition.FromJson(row.SchemaJson);
            }
            catch (Exception)
            {
                // A corrupt layout must not take down registration; fall through
                // to the built-in default and let Settings > Forms show the error.
            }
        }

        return entity == FormEntityKind.Visitor ? FormDefaults.Visitor() : FormDefaults.Exhibitor();
    }

    public async Task<FormSchema?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.FormSchemas.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public async Task<IReadOnlyList<FormSchema>> ListAsync(FormEntityKind entity, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.FormSchemas
            .AsNoTracking()
            .Where(f => f.Entity == (FormEntity)entity)
            .OrderByDescending(f => f.IsActive).ThenByDescending(f => f.Version)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Save a rearranged layout as the next version and make it live.
    /// Returns the layout problems if it will not do, having changed nothing.
    /// </summary>
    public async Task<(bool Saved, IReadOnlyList<string> Problems, int? SchemaId)> SaveAsync(
        FormDefinition definition, string? user, bool activate = true, CancellationToken ct = default)
    {
        var problems = FormValidator.ValidateLayout(definition);
        if (problems.Count > 0) return (false, problems, null);

        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = (FormEntity)definition.Entity;

        int version = await db.FormSchemas
            .Where(f => f.Entity == entity && f.Name == definition.Name)
            .Select(f => (int?)f.Version)
            .MaxAsync(ct) ?? 0;

        var row = new FormSchema
        {
            Entity = entity,
            Name = string.IsNullOrWhiteSpace(definition.Name) ? $"{definition.Entity} form" : definition.Name,
            Version = version + 1,
            SchemaJson = definition.ToJson(),
            IsActive = false,
        };
        db.FormSchemas.Add(row);
        await db.SaveChangesAsync(ct);

        if (activate) await ActivateAsync(row.Id, user, ct);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = "form.save",
            EntityName = "FormSchema",
            EntityId = row.Id.ToString(),
            User = user,
            DetailJson = $"{{\"entity\":\"{definition.Entity}\",\"version\":{row.Version}}}",
        });
        await db.SaveChangesAsync(ct);

        return (true, [], row.Id);
    }

    public async Task ActivateAsync(int schemaId, string? user, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var target = await db.FormSchemas.FirstOrDefaultAsync(f => f.Id == schemaId, ct)
            ?? throw new InvalidOperationException($"no form schema {schemaId}");

        // Deactivate first and save, or the filtered unique index rejects the
        // moment two rows are briefly active at once.
        var current = await db.FormSchemas
            .Where(f => f.Entity == target.Entity && f.IsActive && f.Id != schemaId)
            .ToListAsync(ct);

        foreach (var row in current) row.IsActive = false;
        if (current.Count > 0) await db.SaveChangesAsync(ct);

        target.IsActive = true;
        target.UpdatedUtc = DateTime.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Action = "form.activate",
            EntityName = "FormSchema",
            EntityId = schemaId.ToString(),
            User = user,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Install the built-in layouts on a fresh database.</summary>
    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.FormSchemas.AnyAsync(ct)) return;

        foreach (var definition in new[] { FormDefaults.Visitor(), FormDefaults.Exhibitor() })
        {
            db.FormSchemas.Add(new FormSchema
            {
                Entity = (FormEntity)definition.Entity,
                Name = definition.Name,
                Version = 1,
                SchemaJson = definition.ToJson(),
                IsActive = true,
                Notes = "Installed with the system. Rearrange it in Settings > Forms.",
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
