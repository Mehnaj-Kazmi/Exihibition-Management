namespace Exb.Core.Geometry;

/// <summary>
/// An axis-aligned floor rectangle in hall-local metres. Stand footprints are
/// rectangles in every exhibition floor plan we care about, and keeping them
/// axis-aligned makes attribution a couple of comparisons instead of a
/// polygon test on the hot path.
/// </summary>
public readonly record struct FloorRect(double X, double Y, double Width, double Depth)
{
    public double Right => X + Width;
    public double Top => Y + Depth;
    public double CentreX => X + Width / 2.0;
    public double CentreY => Y + Depth / 2.0;
    public double Area => Width * Depth;

    public bool Contains(double px, double py)
        => px >= X && px <= Right && py >= Y && py <= Top;

    /// <summary>Grow the rectangle outward by <paramref name="m"/> metres on every side.</summary>
    public FloorRect Expand(double m) => new(X - m, Y - m, Width + 2 * m, Depth + 2 * m);

    /// <summary>
    /// Shortest distance from a point to the rectangle. Zero inside it, which is
    /// what attribution wants: a badge standing anywhere on the stand is equally
    /// "at" that stand, and only outside does distance start to discriminate.
    /// </summary>
    public double DistanceTo(double px, double py)
    {
        double dx = Math.Max(Math.Max(X - px, 0.0), px - Right);
        double dy = Math.Max(Math.Max(Y - py, 0.0), py - Top);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public bool Intersects(FloorRect o)
        => X < o.Right && Right > o.X && Y < o.Top && Top > o.Y;
}

/// <summary>
/// Human-readable wayfinding labels. Stewards and visitors navigate by "aisle
/// D, block 7", not by metres, so every position carries a zone label.
/// </summary>
public static class ZoneGrid
{
    /// <summary>I and O are omitted: they are unreadable on hanging floor signage.</summary>
    public const string ColumnLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static int ColumnCount(double hallWidthM, double zoneSizeM)
        => Math.Max(1, (int)Math.Ceiling(hallWidthM / zoneSizeM));

    public static int RowCount(double hallDepthM, double zoneSizeM)
        => Math.Max(1, (int)Math.Ceiling(hallDepthM / zoneSizeM));

    public static string Label(double x, double y, double hallWidthM, double hallDepthM, double zoneSizeM)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) return "?";
        int cols = ColumnCount(hallWidthM, zoneSizeM);
        int rows = RowCount(hallDepthM, zoneSizeM);
        int c = Math.Clamp((int)Math.Floor(x / zoneSizeM), 0, cols - 1);
        int r = Math.Clamp((int)Math.Floor(y / zoneSizeM), 0, rows - 1);
        char letter = c < ColumnLetters.Length ? ColumnLetters[c] : '?';
        return $"{letter}{r + 1}";
    }

    /// <summary>Centre of a labelled zone, for "walk to here" directions.</summary>
    public static (double X, double Y)? Centre(string label, double zoneSizeM)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length < 2) return null;
        int c = ColumnLetters.IndexOf(char.ToUpperInvariant(label[0]));
        if (c < 0 || !int.TryParse(label[1..], out int row) || row < 1) return null;
        return ((c + 0.5) * zoneSizeM, (row - 0.5) * zoneSizeM);
    }

    /// <summary>Compass bearing between two floor points, as an eight-point label.</summary>
    public static string Bearing(double fromX, double fromY, double toX, double toY)
    {
        double dx = toX - fromX, dy = toY - fromY;
        if (Math.Sqrt(dx * dx + dy * dy) < 0.5) return "here";
        double deg = Math.Atan2(dx, dy) * 180.0 / Math.PI; // 0 = north (+y)
        string[] dirs = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        return dirs[(int)Math.Round((deg + 360.0) % 360.0 / 45.0) % 8];
    }
}
