namespace Exb.Data.Services;

/// <summary>
/// Where uploaded e-catalogues and built packs live on disk.
///
/// Files are kept on the filesystem rather than in SQL Server: a 40 MB PDF per
/// exhibitor turns into a database nobody can back up quickly, and the packs are
/// rebuilt from source files anyway, so they are cheap to lose.
/// </summary>
public sealed class CatalogueStorage(string rootPath)
{
    public string Root { get; } = Path.GetFullPath(rootPath);

    public string CatalogueDirectory => EnsureDirectory(Path.Combine(Root, "catalogues"));
    public string PackDirectory => EnsureDirectory(Path.Combine(Root, "packs"));

    public string CataloguePathFor(int exhibitorId, string fileName)
        => Path.Combine(EnsureDirectory(Path.Combine(CatalogueDirectory, exhibitorId.ToString())), fileName);

    public string PackPathFor(DateOnly day, int visitorId, string token)
        => Path.Combine(
            EnsureDirectory(Path.Combine(PackDirectory, day.ToString("yyyy-MM-dd"))),
            $"visitor-{visitorId}-{token}.zip");

    /// <summary>
    /// Resolve a stored relative path to an absolute one, refusing anything that
    /// escapes the storage root. Paths reach here from database rows, and a
    /// traversal in one of them would otherwise let a pack pull in arbitrary
    /// server files and email them to a visitor.
    /// </summary>
    public string? ResolveStored(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        string full = Path.GetFullPath(Path.IsPathRooted(storedPath)
            ? storedPath
            : Path.Combine(Root, storedPath));

        return full.StartsWith(Root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>Store a path relative to the root, so the install can be moved.</summary>
    public string ToRelative(string absolutePath)
        => Path.GetRelativePath(Root, absolutePath);

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
