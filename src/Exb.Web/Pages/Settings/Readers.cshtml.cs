using Exb.Core.Facility;
using Exb.Core.Tracking;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// Maps the readers the system derived from the floor plan onto real network
/// addresses.
///
/// Which antennas hang off which reader is derived from the stand layout and
/// cannot be edited here; the addresses are the one part that depends on how the
/// site was actually cabled. Configuring even one endpoint switches the system
/// off the simulator entirely.
/// </summary>
public class ReadersModel(
    IDbContextFactory<ExhibitionDbContext> factory,
    FacilityProvider facility,
    TrackingRuntime runtime) : PageModel
{
    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public int Configured { get; private set; }
    public string DriverName => runtime.DriverName;
    public string? Message { get; private set; }

    public record Row(
        string ReaderCode, string HallName, AntennaKind Kind, int Antennas,
        string? Host, int Port, bool IsEnabled, ReaderState State, string? Detail);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostSaveAsync(
        string readerCode, string? host, int port, bool isEnabled, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.ReaderEndpoints.FirstOrDefaultAsync(r => r.ReaderCode == readerCode, ct);
        host = (host ?? "").Trim();

        if (string.IsNullOrEmpty(host))
        {
            if (row is not null) db.ReaderEndpoints.Remove(row);
        }
        else
        {
            if (row is null)
            {
                row = new ReaderEndpoint { ReaderCode = readerCode };
                db.ReaderEndpoints.Add(row);
            }
            row.Host = host;
            row.Port = port <= 0 ? 5084 : port;
            row.IsEnabled = isEnabled;
            row.UpdatedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        // Endpoints decide which driver runs, so the change only takes effect on a restart.
        await runtime.RestartAsync(ct);

        TempData["message"] = $"{readerCode} updated. Tracking restarted using {runtime.DriverName}.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Message = TempData["message"] as string;

        await using var db = await factory.CreateDbContextAsync(ct);
        var endpoints = await db.ReaderEndpoints.AsNoTracking()
            .ToDictionaryAsync(r => r.ReaderCode, StringComparer.OrdinalIgnoreCase, ct);

        Configured = endpoints.Count(e => e.Value.IsEnabled && !string.IsNullOrWhiteSpace(e.Value.Host));

        var statuses = runtime.ReaderStatuses
            .ToDictionary(s => s.ReaderCode, StringComparer.OrdinalIgnoreCase);

        var model = facility.Current;

        Rows = model.Readers
            .OrderBy(r => r.HallCode).ThenBy(r => r.Code)
            .Select(r =>
            {
                endpoints.TryGetValue(r.Code, out var endpoint);
                statuses.TryGetValue(r.Code, out var status);
                var hall = model.HallById.GetValueOrDefault(r.HallId);

                return new Row(
                    r.Code, hall?.Name ?? r.HallCode, r.Kind, r.AntennaCodes.Count,
                    endpoint?.Host, endpoint?.Port ?? 5084, endpoint?.IsEnabled ?? true,
                    status?.State ?? ReaderState.Offline, status?.Detail);
            })
            .ToList();
    }
}
