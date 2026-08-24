using System.Text.Json;
using Exb.Core.Configuration;
using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data.Services;

/// <summary>Every settings group, as one immutable snapshot.</summary>
public sealed class AppSettings
{
    public ExhibitionSettings Exhibition { get; init; } = new();
    public TrackingSettings Tracking { get; init; } = new();
    public DwellSettings Dwell { get; init; } = new();
    public DeliverySettings Delivery { get; init; } = new();
    public MailSettings Mail { get; init; } = new();
    public SimulatorSettings Simulator { get; init; } = new();

    public AppSettings With(string key, object value) => new()
    {
        Exhibition = key == SettingsKeys.Exhibition ? (ExhibitionSettings)value : Exhibition,
        Tracking = key == SettingsKeys.Tracking ? (TrackingSettings)value : Tracking,
        Dwell = key == SettingsKeys.Dwell ? (DwellSettings)value : Dwell,
        Delivery = key == SettingsKeys.Delivery ? (DeliverySettings)value : Delivery,
        Mail = key == SettingsKeys.Mail ? (MailSettings)value : Mail,
        Simulator = key == SettingsKeys.Simulator ? (SimulatorSettings)value : Simulator,
    };
}

public static class SettingsKeys
{
    public const string Exhibition = "exhibition";
    public const string Tracking = "tracking";
    public const string Dwell = "dwell";
    public const string Delivery = "delivery";
    public const string Mail = "mail";
    public const string Simulator = "simulator";
}

/// <summary>
/// Loads and saves the admin-editable configuration.
///
/// It lives in SQL Server rather than appsettings.json because the settings
/// screens have to work while the exhibition is running — an organiser adding a
/// hall on the Tuesday morning cannot be asked to edit a file and restart the
/// site. The current snapshot is cached and swapped atomically, so a half-saved
/// change is never visible to the tracking stack.
/// </summary>
public sealed class SettingsStore(IDbContextFactory<ExhibitionDbContext> factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private AppSettings _current = new();

    /// <summary>Raised after a save, so the facility model and drivers can react.</summary>
    public event Action<string, AppSettings>? Changed;

    public AppSettings Current => _current;

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.ValueJson, ct);

        _current = new AppSettings
        {
            Exhibition = Read<ExhibitionSettings>(rows, SettingsKeys.Exhibition),
            Tracking = Read<TrackingSettings>(rows, SettingsKeys.Tracking),
            Dwell = Read<DwellSettings>(rows, SettingsKeys.Dwell),
            Delivery = Read<DeliverySettings>(rows, SettingsKeys.Delivery),
            Mail = Read<MailSettings>(rows, SettingsKeys.Mail),
            Simulator = Read<SimulatorSettings>(rows, SettingsKeys.Simulator),
        };
        return _current;
    }

    public async Task SaveAsync<T>(string key, T value, string? user, CancellationToken ct = default) where T : notnull
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        string json = JsonSerializer.Serialize(value);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (row is null)
            db.Settings.Add(new SettingEntry { Key = key, ValueJson = json, UpdatedBy = user, UpdatedUtc = DateTime.UtcNow });
        else
        {
            row.ValueJson = json;
            row.UpdatedBy = user;
            row.UpdatedUtc = DateTime.UtcNow;
        }

        db.AuditEntries.Add(new AuditEntry
        {
            Action = "settings.save",
            EntityName = "Setting",
            EntityId = key,
            User = user,
            DetailJson = json,
        });

        await db.SaveChangesAsync(ct);

        _current = _current.With(key, value);
        Changed?.Invoke(key, _current);
    }

    /// <summary>Write defaults for anything not yet in the table, on first run.</summary>
    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Settings.Select(s => s.Key).ToListAsync(ct);

        var defaults = new (string Key, object Value)[]
        {
            (SettingsKeys.Exhibition, new ExhibitionSettings()),
            (SettingsKeys.Tracking, new TrackingSettings()),
            (SettingsKeys.Dwell, new DwellSettings()),
            (SettingsKeys.Delivery, new DeliverySettings()),
            (SettingsKeys.Mail, new MailSettings()),
            (SettingsKeys.Simulator, new SimulatorSettings()),
        };

        foreach (var (key, value) in defaults)
        {
            if (existing.Contains(key)) continue;
            db.Settings.Add(new SettingEntry { Key = key, ValueJson = JsonSerializer.Serialize(value) });
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        await LoadAsync(ct);
    }

    private static T Read<T>(IReadOnlyDictionary<string, string> rows, string key) where T : new()
    {
        if (!rows.TryGetValue(key, out string? json) || string.IsNullOrWhiteSpace(json)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? new T();
        }
        catch (JsonException)
        {
            // A settings row that cannot be read must not stop the site booting;
            // fall back to defaults and let the settings screen show the problem.
            return new T();
        }
    }
}
