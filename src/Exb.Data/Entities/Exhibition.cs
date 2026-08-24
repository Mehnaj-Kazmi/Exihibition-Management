using System.ComponentModel.DataAnnotations;

namespace Exb.Data.Entities;

/// <summary>
/// A hall. Admin-managed from Settings > Halls: halls can be added, removed and
/// resized while the exhibition runs, which is why this lives in the database
/// rather than in appsettings.json.
/// </summary>
public class Hall
{
    public int Id { get; set; }

    [MaxLength(16)] public string Code { get; set; } = "";
    [MaxLength(120)] public string Name { get; set; } = "";

    public double WidthM { get; set; } = 60;
    public double DepthM { get; set; } = 40;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(400)] public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<Kiosk> Kiosks { get; set; } = [];

    public double AreaM2 => WidthM * DepthM;
}

/// <summary>
/// Two-level product taxonomy. A category with a null parent is a top-level
/// category; its children are its sub-categories. Interest is rolled up at
/// both levels, and the missed-stand recommendation walks the same tree.
/// </summary>
public class Category
{
    public int Id { get; set; }

    [MaxLength(32)] public string Code { get; set; } = "";
    [MaxLength(120)] public string Name { get; set; } = "";

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = [];

    /// <summary>Hex colour used for the category on the live floor plan and in reports.</summary>
    [MaxLength(9)] public string? Colour { get; set; }

    [MaxLength(400)] public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Exhibitor> Exhibitors { get; set; } = [];
}

/// <summary>
/// An exhibiting company. The fixed columns are the ones the system itself
/// reasons about (category, contact, e-catalogue routing); everything else the
/// organiser wants to collect for a particular exhibition lives in
/// <see cref="ProfileJson"/>, shaped by the active exhibitor form schema.
/// </summary>
public class Exhibitor
{
    public int Id { get; set; }

    [MaxLength(32)] public string Code { get; set; } = "";
    [MaxLength(200)] public string CompanyName { get; set; } = "";

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? SubCategoryId { get; set; }
    public Category? SubCategory { get; set; }

    [MaxLength(200)] public string? ContactName { get; set; }
    [MaxLength(320)] public string? Email { get; set; }
    [MaxLength(60)] public string? Phone { get; set; }
    [MaxLength(300)] public string? Website { get; set; }
    [MaxLength(120)] public string? Country { get; set; }
    [MaxLength(1000)] public string? Summary { get; set; }
    [MaxLength(400)] public string? LogoPath { get; set; }

    /// <summary>Answers to the admin-defined exhibitor form, as a JSON object.</summary>
    public string ProfileJson { get; set; } = "{}";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public List<Kiosk> Kiosks { get; set; } = [];
    public List<CatalogueAsset> Catalogues { get; set; } = [];
}

/// <summary>
/// A stand on the floor. Its size drives how many antennas get mounted on it,
/// and its QR token is what a visitor's phone resolves when they scan for the
/// e-catalogue.
/// </summary>
public class Kiosk
{
    public int Id { get; set; }

    public int ExhibitorId { get; set; }
    public Exhibitor Exhibitor { get; set; } = null!;

    public int HallId { get; set; }
    public Hall Hall { get; set; } = null!;

    [MaxLength(24)] public string StandNumber { get; set; } = "";

    /// <summary>South-west corner of the stand, in hall-local metres.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double WidthM { get; set; } = 3;
    public double DepthM { get; set; } = 3;

    /// <summary>
    /// Opaque token embedded in the stand's QR code. Random rather than the id,
    /// so a printed QR cannot be used to enumerate the exhibitor list.
    /// </summary>
    [MaxLength(32)] public string QrToken { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<VisitorVisit> Visits { get; set; } = [];
    public List<CatalogueRequest> CatalogueRequests { get; set; } = [];

    public double AreaM2 => WidthM * DepthM;
}

/// <summary>A downloadable e-catalogue file belonging to an exhibitor.</summary>
public class CatalogueAsset
{
    public int Id { get; set; }

    public int ExhibitorId { get; set; }
    public Exhibitor Exhibitor { get; set; } = null!;

    [MaxLength(260)] public string FileName { get; set; } = "";
    [MaxLength(160)] public string ContentType { get; set; } = "application/pdf";
    public long SizeBytes { get; set; }

    /// <summary>Path under the configured catalogue storage root.</summary>
    [MaxLength(400)] public string StoragePath { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One day of the exhibition. End-of-day processing runs per day, and reports
/// are scoped to a day, so the days are explicit rather than inferred.
/// </summary>
public class EventDay
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }
    [MaxLength(120)] public string? Name { get; set; }

    public TimeOnly OpensAt { get; set; } = new(10, 0);
    public TimeOnly ClosesAt { get; set; } = new(18, 0);

    /// <summary>Set once end-of-day packs and reports have been generated.</summary>
    public bool Closed { get; set; }
    public DateTime? ClosedUtc { get; set; }
}
