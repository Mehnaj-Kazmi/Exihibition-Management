using System.Collections.Concurrent;
using Exb.Core.Configuration;

namespace Exb.Core.Tracking;

/// <summary>
/// UHF RFID link model for downward-facing antennas.
///
/// The physical model has two terms:
///
///   path loss   P0 - 10*n*log10(d)              d = slant range, metres
///   beam shape  2 * 10*k*log10(cos(theta))      theta = angle off boresight,
///                                               doubled for the round trip
///
/// Because the antennas point straight down, cos(theta) = dz/d for every badge
/// at lanyard height. Substituting that in collapses the beam term into the
/// path-loss term:
///
///   E(d) = P0 - 10n*log10(d) + 20k*log10(dz/d)
///        = [P0 + 20k*log10(dz)] - [10n + 20k]*log10(d)
///        = A - B*log10(d)
///
/// So the whole link is a single log-distance law with an effective exponent B
/// far steeper than free space. That matters because inversion becomes exact
/// and closed form, the derivative is analytic (so Gauss-Newton gets a true
/// Jacobian), and the read radius cuts off sharply.
///
/// Unlike the warehouse version this was ported from, the coefficients are
/// resolved per antenna height rather than once globally: stand-mounted
/// antennas hang at about 3.2 m and any aisle grid at about 6 m, and both sets
/// can hear the same badge. A single global height would have quietly biased
/// every mixed fix.
/// </summary>
public sealed class RfModel
{
    private readonly RfSettings _rf;
    private readonly ConcurrentDictionary<double, Coefficients> _cache = new();

    public RfModel(RfSettings rf)
    {
        _rf = rf ?? throw new ArgumentNullException(nameof(rf));
        if (_rf.PathLossExponent <= 0) throw new ArgumentException("PathLossExponent must be > 0", nameof(rf));
    }

    public RfSettings Settings => _rf;

    /// <summary>The two constants of the log-distance law for one mounting height.</summary>
    public readonly record struct Coefficients(double A, double B, double Dz, double Dz2);

    public Coefficients CoefficientsFor(double antennaHeightM)
    {
        return _cache.GetOrAdd(antennaHeightM, h =>
        {
            double dz = h - _rf.TagHeightM;
            if (dz <= 0)
                throw new ArgumentException(
                    $"antenna height {h} m must be above the tag height {_rf.TagHeightM} m", nameof(antennaHeightM));

            double a = _rf.RefRssiAt1M + 20.0 * _rf.BeamExponent * Math.Log10(dz);
            double b = 10.0 * _rf.PathLossExponent + 20.0 * _rf.BeamExponent;
            return new Coefficients(a, b, dz, dz * dz);
        });
    }

    /// <summary>Expected RSSI (dBm) for a badge <paramref name="lateralM"/> metres from the antenna's floor projection.</summary>
    public double ExpectedRssi(double lateralM, double antennaHeightM)
    {
        var c = CoefficientsFor(antennaHeightM);
        double d = Math.Sqrt(lateralM * lateralM + c.Dz2);
        return c.A - c.B * Math.Log10(d);
    }

    /// <summary>
    /// d(RSSI)/d(lateral) in dB per metre. Exact analytic derivative.
    ///
    /// It goes to zero as lateral goes to zero: directly beneath an antenna the
    /// signal sits at a stationary maximum, so RSSI says almost nothing about
    /// which way the badge is offset. The locator's covariance picks this up on
    /// its own and widens the uncertainty there, which is the honest answer.
    /// </summary>
    public double DRssiDLateral(double lateralM, double antennaHeightM)
    {
        var c = CoefficientsFor(antennaHeightM);
        double d2 = lateralM * lateralM + c.Dz2;
        return -c.B * lateralM / (Math.Log(10.0) * d2);
    }

    /// <summary>Invert the law: slant range implied by a measured RSSI. Exact.</summary>
    public double SlantRangeFromRssi(double rssiDbm, double antennaHeightM)
    {
        var c = CoefficientsFor(antennaHeightM);
        return Math.Max(c.Dz, Math.Pow(10.0, (c.A - rssiDbm) / c.B));
    }

    /// <summary>Horizontal component of a slant range.</summary>
    public double LateralFromSlant(double slantM, double antennaHeightM)
    {
        var c = CoefficientsFor(antennaHeightM);
        return Math.Sqrt(Math.Max(0.0, slantM * slantM - c.Dz2));
    }

    /// <summary>Lateral distance implied by a measured RSSI.</summary>
    public double LateralFromRssi(double rssiDbm, double antennaHeightM)
        => LateralFromSlant(SlantRangeFromRssi(rssiDbm, antennaHeightM), antennaHeightM);

    /// <summary>Largest lateral offset that still produces a readable badge. Closed form.</summary>
    public double MaxLateralRange(double antennaHeightM)
        => LateralFromRssi(_rf.SensitivityDbm, antennaHeightM);

    /// <summary>
    /// One-sigma lateral uncertainty from a single antenna's read, by propagating
    /// RSSI noise through the inverse law. Blows up near boresight, per the note
    /// on <see cref="DRssiDLateral"/>.
    /// </summary>
    public double LateralSigma(double lateralM, double antennaHeightM, double rssiNoiseDb)
    {
        double slope = Math.Abs(DRssiDLateral(lateralM, antennaHeightM));
        return slope < 1e-6 ? 99.0 : rssiNoiseDb / slope;
    }
}
