using Exb.Core.Facility;

namespace Exb.Core.Dwell;

/// <summary>
/// Which stand a badge is standing at, and how sure we are that it is that one
/// rather than the stand next door.
/// </summary>
/// <param name="KioskId">Winning stand.</param>
/// <param name="DistanceM">Distance from the badge to that stand's footprint. Zero when standing on it.</param>
/// <param name="MarginM">
/// How much closer the winner is than the runner-up. This is the number that
/// decides whether the answer is trustworthy: on a row of 3 m shell schemes a
/// margin of a few centimetres means "one of these two", and the system should
/// say so rather than pick.
/// </param>
/// <param name="RunnerUpKioskId">The stand that was nearly chosen, if any.</param>
public readonly record struct KioskAttribution(int KioskId, double DistanceM, double MarginM, int? RunnerUpKioskId);

public static class KioskAttributor
{
    /// <summary>
    /// Attribute a floor position to a stand.
    ///
    /// Distance is measured to the stand's footprint rather than to its centre.
    /// That matters more than it sounds: visitors stand at the open edge of a
    /// stand, and a large island stand's centre can be five metres from where
    /// anyone actually stands, so centre distance would systematically hand
    /// visitors to the small stand across the aisle.
    /// </summary>
    public static KioskAttribution? Attribute(
        FacilityHall hall, double x, double y, double attachRadiusM)
    {
        double bestDistance = double.MaxValue, runnerUpDistance = double.MaxValue;
        int bestId = 0;
        int? runnerUpId = null;

        foreach (var kiosk in hall.Kiosks)
        {
            double d = kiosk.Footprint.DistanceTo(x, y);
            if (d < bestDistance)
            {
                runnerUpDistance = bestDistance;
                runnerUpId = bestId == 0 ? null : bestId;
                bestDistance = d;
                bestId = kiosk.Id;
            }
            else if (d < runnerUpDistance)
            {
                runnerUpDistance = d;
                runnerUpId = kiosk.Id;
            }
        }

        if (bestId == 0 || bestDistance > attachRadiusM) return null;

        // With no second stand anywhere near, the attribution is unambiguous;
        // report the full attach radius as the margin rather than infinity.
        double margin = runnerUpId is null
            ? attachRadiusM
            : Math.Min(attachRadiusM, runnerUpDistance - bestDistance);

        return new KioskAttribution(bestId, Math.Round(bestDistance, 2), Math.Round(margin, 2), runnerUpId);
    }
}
