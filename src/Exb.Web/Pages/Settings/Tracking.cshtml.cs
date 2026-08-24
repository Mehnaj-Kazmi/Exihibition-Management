using Exb.Core.Configuration;
using Exb.Core.Facility;
using Exb.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// The numbers that decide what counts as interest, and how well it can be
/// measured.
///
/// Saving here rebuilds the facility model and restarts the reader drivers,
/// because antenna density, mounting height and the RF model all change the
/// physical layout rather than just a display preference.
/// </summary>
public class TrackingModel(
    SettingsStore settings,
    FacilityProvider facility) : PageModel
{
    [BindProperty] public TrackingSettings Tracking { get; set; } = new();
    [BindProperty] public DwellSettings Dwell { get; set; } = new();
    [BindProperty] public SimulatorSettings Simulator { get; set; } = new();

    public CoverageReport Coverage => facility.Current.Coverage;
    public string? Message { get; private set; }
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>Read radius implied by the current mounting heights, so the effect is visible before saving.</summary>
    public double KioskRadius { get; private set; }
    public double AisleRadius { get; private set; }

    public void OnGet()
    {
        Load();
        Message = TempData["message"] as string;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var problems = Validate();
        if (problems.Count > 0)
        {
            Problems = problems;
            ComputeRadii();
            return Page();
        }

        await settings.SaveAsync(SettingsKeys.Tracking, Tracking, User.Identity?.Name, ct);
        await settings.SaveAsync(SettingsKeys.Dwell, Dwell, User.Identity?.Name, ct);
        await settings.SaveAsync(SettingsKeys.Simulator, Simulator, User.Identity?.Name, ct);

        var model = await facility.RebuildAsync(ct);

        TempData["message"] =
            $"Saved. The floor now has {model.Coverage.TotalAntennas} antenna(s) with a "
            + $"{model.Coverage.KioskReadRadiusM} m read radius from each stand antenna, and "
            + $"{model.Coverage.StandFloorLocalizablePct}% of stand floor has the three antennas a full fix needs. "
            + "Tracking is restarting with the new layout.";

        return RedirectToPage();
    }

    /// <summary>
    /// Refuse configurations that would look fine and then produce nonsense.
    /// Each message says what would go wrong, not merely that a value is out of
    /// range.
    /// </summary>
    private List<string> Validate()
    {
        var problems = new List<string>();

        if (Tracking.KioskAntennas.HeightM <= Tracking.Rf.TagHeightM)
            problems.Add($"Stand antennas at {Tracking.KioskAntennas.HeightM} m are at or below badge height "
                + $"({Tracking.Rf.TagHeightM} m). The link model needs them above the badges.");

        if (Tracking.AisleGrid.Enabled && Tracking.AisleGrid.HeightM <= Tracking.Rf.TagHeightM)
            problems.Add($"Aisle antennas at {Tracking.AisleGrid.HeightM} m are at or below badge height.");

        if (Tracking.Rf.PathLossExponent <= 0)
            problems.Add("Path-loss exponent must be greater than zero.");

        if (Tracking.KioskAntennas.AreaPerAntennaM2 <= 0)
            problems.Add("Area per antenna must be greater than zero, or no stand gets any antennas.");

        if (Tracking.KioskAntennas.MinPerKiosk < 1)
            problems.Add("Every stand needs at least one antenna, or no interest can be recorded on it.");

        if (Tracking.KioskAntennas.MaxPerKiosk < Tracking.KioskAntennas.MinPerKiosk)
            problems.Add("Maximum antennas per stand cannot be below the minimum.");

        if (Dwell.MinDwellSeconds >= Dwell.InterestSeconds)
            problems.Add("The interest threshold must be longer than the minimum dwell, or every recorded stop becomes interest.");

        if (Dwell.InterestSeconds >= Dwell.StrongSeconds)
            problems.Add("The strong-interest threshold must be longer than the interest threshold.");

        if (Dwell.BreakSeconds < 5)
            problems.Add("A break shorter than 5 seconds would split one visit into many, because badges are not heard continuously.");

        if (Dwell.MaxSessionSeconds < Dwell.StrongSeconds)
            problems.Add("The session cap is below the strong-interest threshold, so no visit could ever reach it.");

        if (Dwell.AttachRadiusM < 0)
            problems.Add("Attach radius cannot be negative.");

        if (Tracking.Locator.WindowMs < Tracking.Locator.IntervalMs)
            problems.Add("The read window must be at least as long as the solve interval, or each solve sees only part of a port cycle.");

        return problems;
    }

    private void Load()
    {
        var current = settings.Current;
        Tracking = current.Tracking.Clone();
        Dwell = current.Dwell.Clone();
        Simulator = current.Simulator.Clone();
        ComputeRadii();
    }

    private void ComputeRadii()
    {
        try
        {
            var rf = new Core.Tracking.RfModel(Tracking.Rf);
            KioskRadius = Math.Round(rf.MaxLateralRange(Tracking.KioskAntennas.HeightM), 2);
            AisleRadius = Tracking.AisleGrid.Enabled
                ? Math.Round(rf.MaxLateralRange(Tracking.AisleGrid.HeightM), 2)
                : 0;
        }
        catch (ArgumentException)
        {
            KioskRadius = AisleRadius = 0;   // an invalid height; Validate reports it properly
        }
    }
}
