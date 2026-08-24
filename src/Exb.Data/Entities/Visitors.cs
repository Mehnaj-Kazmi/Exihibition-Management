using System.ComponentModel.DataAnnotations;

namespace Exb.Data.Entities;

/// <summary>How much interest a dwell session represents.</summary>
public enum InterestLevel
{
    /// <summary>Walked past or paused briefly. Recorded, but not treated as interest.</summary>
    PassBy = 0,

    /// <summary>Stopped long enough to look. The weakest signal we call real.</summary>
    Browsed = 1,

    /// <summary>Stayed and engaged. This is what the daily report leads with.</summary>
    Interested = 2,

    /// <summary>A long stop — a demo or a conversation.</summary>
    Strong = 3,
}

/// <summary>
/// A registered attendee and the badge tag they carry. The fixed columns are
/// the ones the system acts on — the email the pack is sent to, the EPC that
/// identifies the badge — and the rest of the registration form lands in
/// <see cref="ProfileJson"/>.
/// </summary>
public class Visitor
{
    public int Id { get; set; }

    /// <summary>EPC of the RFID inlay in the entry badge. The link between a person and the floor.</summary>
    [MaxLength(64)] public string BadgeEpc { get; set; } = "";

    /// <summary>Short human-readable code printed on the badge, for the help desk.</summary>
    [MaxLength(24)] public string RegistrationCode { get; set; } = "";

    [MaxLength(200)] public string FullName { get; set; } = "";
    [MaxLength(320)] public string Email { get; set; } = "";
    [MaxLength(60)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Company { get; set; }
    [MaxLength(160)] public string? JobTitle { get; set; }
    [MaxLength(120)] public string? Country { get; set; }

    /// <summary>Answers to the admin-defined visitor registration form, as a JSON object.</summary>
    public string ProfileJson { get; set; } = "{}";

    /// <summary>Consent to receive the e-catalogue pack and daily report by email.</summary>
    public bool ConsentEmail { get; set; } = true;

    /// <summary>
    /// Consent to have stand dwell time recorded. Without it the badge is still
    /// located for safety and headcount, but no visit rows are written and the
    /// visitor gets no interest report.
    /// </summary>
    public bool ConsentTracking { get; set; } = true;

    /// <summary>Preferred language for the daily report, as an ISO code.</summary>
    [MaxLength(8)] public string? Language { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Token used by the visitor's own phone page, so no password is needed.</summary>
    [MaxLength(32)] public string AccessToken { get; set; } = "";

    public List<VisitorVisit> Visits { get; set; } = [];
    public List<CatalogueRequest> CatalogueRequests { get; set; } = [];
}

/// <summary>
/// One continuous stop at one stand: the atom that interest is built from.
///
/// A session is opened when a badge is attributed to a stand and closed when it
/// leaves or goes quiet. The quality columns are kept because attribution is a
/// physical inference, not a fact: a short session recorded from weak, widely
/// uncertain fixes should not carry the same weight as a ten-minute stop
/// measured from four antennas on the stand itself.
/// </summary>
public class VisitorVisit
{
    public long Id { get; set; }

    public int VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    public int KioskId { get; set; }
    public Kiosk Kiosk { get; set; } = null!;

    /// <summary>Denormalised so day reports and category rollups do not need a four-table join.</summary>
    public int ExhibitorId { get; set; }
    public int HallId { get; set; }
    public int? CategoryId { get; set; }
    public int? SubCategoryId { get; set; }

    public DateOnly EventDate { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public int DwellSeconds { get; set; }

    public InterestLevel Level { get; set; }

    /// <summary>Number of position solves attributed to this stand during the session.</summary>
    public int SampleCount { get; set; }

    /// <summary>Mean locating confidence across those solves, 0..1.</summary>
    public double MeanConfidence { get; set; }

    /// <summary>
    /// Mean margin, in metres, by which this stand beat the runner-up stand.
    /// Small margins on a crowded row mean the attribution is genuinely
    /// ambiguous, and the report says so rather than pretending otherwise.
    /// </summary>
    public double MeanMarginM { get; set; }

    /// <summary>True while the visitor is still standing there.</summary>
    public bool IsOpen { get; set; }
}

/// <summary>
/// A visitor scanning a stand's QR code to request its e-catalogue. Every scan
/// during the day is collected into one zip and sent that evening.
/// </summary>
public class CatalogueRequest
{
    public long Id { get; set; }

    public int VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    public int KioskId { get; set; }
    public Kiosk Kiosk { get; set; } = null!;

    public int ExhibitorId { get; set; }

    public DateOnly EventDate { get; set; }
    public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>"qr" for a scanned code, "app" from the visitor page, "staff" if added at a desk.</summary>
    [MaxLength(16)] public string Source { get; set; } = "qr";

    /// <summary>Included in the evening pack. Cleared if the visitor removes it from their list.</summary>
    public bool Included { get; set; } = true;
}
