using Exb.Core.Configuration;

namespace Exb.Core.Tracking;

/// <summary>One antenna's contribution to a fix: where it hangs, and what it heard.</summary>
public readonly record struct AntennaFix(string AntennaId, double X, double Y, double HeightM, double Rssi);

/// <summary>A solved badge position with an honest account of how good it is.</summary>
public sealed record PositionFix(
    double X,
    double Y,
    double UncertaintyM,
    double Confidence,
    double? ResidualRms,
    int AntennaCount,
    int Iterations,
    string Method,
    double BestRssi);

/// <summary>
/// Position solver.
///
/// Given the antennas that heard a badge and the RSSI each measured, estimate
/// where the visitor is standing on the hall floor.
///
/// The fit is done in RSSI space: we minimise the weighted squared difference
/// between each antenna's measured RSSI and the RSSI the link model predicts
/// for the current position estimate. Fitting in dB rather than in converted
/// ranges matters, because the measurement noise is Gaussian in dB. Converting
/// each read to a range first would skew those errors and bias the solution
/// toward the antennas that happen to read weakly.
///
/// Because <see cref="RfModel"/> reduces the downward link to a single
/// log-distance law, the residual has an exact analytic Jacobian and
/// Gauss-Newton converges in a handful of iterations.
/// </summary>
public static class Locator
{
    private const double MaxStepM = 5.0;   // trust region; keeps degenerate geometry from diverging
    private const double ConvergedM = 0.01;

    public static PositionFix? Solve(
        RfModel rf,
        LocatorSettings settings,
        double hallWidthM,
        double hallDepthM,
        IReadOnlyList<AntennaFix> fixes)
    {
        if (fixes is null || fixes.Count == 0) return null;

        double sigmaRssi = settings.RssiNoiseDb;
        double w = 1.0 / (sigmaRssi * sigmaRssi); // uniform: noise is homoscedastic in dB

        AntennaFix strongest = fixes[0];
        for (int i = 1; i < fixes.Count; i++)
            if (fixes[i].Rssi > strongest.Rssi) strongest = fixes[i];

        // --- Seed: power-weighted centroid of the hearing antennas ------------
        double sw = 0, px = 0, py = 0;
        foreach (var f in fixes)
        {
            double pw = Math.Pow(10.0, f.Rssi / 10.0);
            sw += pw;
            px += pw * f.X;
            py += pw * f.Y;
        }
        px /= sw;
        py /= sw;

        // A single antenna constrains the badge to a ring around it, with no bearing.
        if (fixes.Count == 1)
        {
            double ring = rf.LateralFromRssi(strongest.Rssi, strongest.HeightM);
            return Finish(hallWidthM, hallDepthM, strongest.X, strongest.Y,
                uncertaintyM: Math.Max(1.5, ring),
                confidence: 0.20,
                residualRms: null,
                antennaCount: 1,
                iterations: 0,
                method: "single-antenna-ring",
                bestRssi: strongest.Rssi);
        }

        // --- Weighted Gauss-Newton on the RSSI residuals ----------------------
        double? residualRms = null;
        double? covTrace = null;
        int iterations = 0;

        for (int iter = 0; iter < settings.MaxIterations; iter++)
        {
            iterations = iter + 1;

            // Normal equations for the 2x2 system: (J^T W J) delta = -(J^T W r)
            double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0, sumSq = 0;

            foreach (var f in fixes)
            {
                double dxa = px - f.X;
                double dya = py - f.Y;
                double lateral = Math.Sqrt(dxa * dxa + dya * dya);

                double residual = rf.ExpectedRssi(lateral, f.HeightM) - f.Rssi;

                // dRSSI/dp = (dRSSI/dlateral) * (dlateral/dp), with dlateral/dp = d/|d|.
                // Written this way the lateral -> 0 singularity cancels analytically.
                double slope = rf.DRssiDLateral(lateral, f.HeightM);
                double jx = lateral < 1e-6 ? 0.0 : slope * (dxa / lateral);
                double jy = lateral < 1e-6 ? 0.0 : slope * (dya / lateral);

                a11 += w * jx * jx;
                a12 += w * jx * jy;
                a22 += w * jy * jy;
                b1 += w * jx * residual;
                b2 += w * jy * residual;
                sumSq += residual * residual;
            }

            residualRms = Math.Sqrt(sumSq / fixes.Count);

            double det = a11 * a22 - a12 * a12;
            if (!double.IsFinite(det) || Math.Abs(det) < 1e-12) break; // collinear or degenerate

            covTrace = (a11 + a22) / det; // trace of inv(J^T W J) = var_x + var_y

            double dx = -(a22 * b1 - a12 * b2) / det;
            double dy = -(a11 * b2 - a12 * b1) / det;

            double step = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(step)) break;
            if (step > MaxStepM)
            {
                dx *= MaxStepM / step;
                dy *= MaxStepM / step;
            }

            px += dx;
            py += dy;

            if (step < ConvergedM) break;
        }

        // --- Quality metrics --------------------------------------------------
        // sqrt(trace(covariance)) is the one-sigma radial position error implied
        // by the geometry and the noise level. It correctly widens directly
        // beneath an antenna and where the hearing antennas are nearly collinear.
        double geometricSigma = covTrace is > 0 && double.IsFinite(covTrace.Value)
            ? Math.Sqrt(covTrace.Value)
            : 4.0;
        double uncertaintyM = Math.Clamp(geometricSigma, 0.2, 10.0);

        // Confidence blends independent signals: how many antennas agreed, how
        // well the model fits, and how tight the geometry is.
        double countScore = Math.Clamp((fixes.Count - 2) / 4.0, 0, 1);
        double fitScore = residualRms is null ? 0.5 : Math.Clamp(1 - residualRms.Value / (3 * sigmaRssi), 0, 1);
        double geomScore = Math.Clamp(1 - (uncertaintyM - 0.2) / 3.0, 0, 1);
        double confidence = Math.Clamp(0.30 * countScore + 0.35 * fitScore + 0.35 * geomScore, 0.05, 0.99);

        return Finish(hallWidthM, hallDepthM, px, py,
            uncertaintyM,
            confidence,
            residualRms,
            fixes.Count,
            iterations,
            fixes.Count == 2 ? "two-antenna" : "multilateration",
            strongest.Rssi);
    }

    private static PositionFix Finish(
        double hallWidthM, double hallDepthM, double x, double y,
        double uncertaintyM, double confidence, double? residualRms,
        int antennaCount, int iterations, string method, double bestRssi)
        => new(
            X: Round2(Math.Clamp(x, 0, hallWidthM)),
            Y: Round2(Math.Clamp(y, 0, hallDepthM)),
            UncertaintyM: Round2(uncertaintyM),
            Confidence: Math.Round(confidence, 3),
            ResidualRms: residualRms is null ? null : Round2(residualRms.Value),
            AntennaCount: antennaCount,
            Iterations: iterations,
            Method: method,
            BestRssi: Round2(bestRssi));

    private static double Round2(double v) => Math.Round(v, 2);

    /// <summary>
    /// Exponential smoothing between successive fixes. A jumpy raw position is
    /// worse than useless on a live floor plan, but we must not smooth so hard
    /// that a visitor genuinely walking to the next stand lags behind. So the
    /// filter opens up when the jump is large enough to be real movement rather
    /// than noise.
    /// </summary>
    public static (double X, double Y) Smooth(
        double? prevX, double? prevY, string? prevHallCode,
        double nextX, double nextY, string nextHallCode,
        double alpha)
    {
        if (prevX is null || prevY is null || prevHallCode != nextHallCode)
            return (nextX, nextY);

        double jump = Math.Sqrt(Math.Pow(nextX - prevX.Value, 2) + Math.Pow(nextY - prevY.Value, 2));
        double a = jump > 3.0 ? Math.Min(1.0, alpha * 2.5) : alpha;

        return (Round2(prevX.Value + a * (nextX - prevX.Value)),
                Round2(prevY.Value + a * (nextY - prevY.Value)));
    }
}
