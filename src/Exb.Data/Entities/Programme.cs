using System.ComponentModel.DataAnnotations;

namespace Exb.Data.Entities;

/// <summary>
/// What kind of thing is on the programme. Visitors filter by this far more than
/// they search by title — "what talks are on this afternoon" is the real query —
/// so it is a column rather than a tag in a JSON blob.
/// </summary>
public enum SessionKind
{
    /// <summary>A talk to an audience: the conference programme.</summary>
    Lecture = 0,

    /// <summary>A scheduled meeting, usually smaller and often bookable.</summary>
    Meeting = 1,

    /// <summary>Hands-on, capacity-limited.</summary>
    Workshop = 2,

    /// <summary>Several speakers and a moderator.</summary>
    Panel = 3,

    /// <summary>A live demonstration, usually on or beside a stand.</summary>
    Demo = 4,

    /// <summary>Opening, closing, awards — the things with no speaker as such.</summary>
    Ceremony = 5,
}

/// <summary>
/// One item on the exhibition programme: a lecture, a meeting, a workshop.
///
/// It sits alongside the stands rather than inside them because the two are
/// genuinely different things to a visitor — a stand is somewhere you walk past,
/// a session is something you have to be at by 14:30 — but they share the same
/// taxonomy, so a visitor interested in RFID can be shown both the stands and the
/// talks in that category from one search.
///
/// The location is either a hall (with a free-text room name for the theatre or
/// meeting room within it) or purely free text, because conference rooms are
/// frequently outside the tracked halls and forcing them onto the floor plan
/// would put fictional coordinates into the facility model.
/// </summary>
public class ProgrammeSession
{
    public int Id { get; set; }

    [MaxLength(32)] public string Code { get; set; } = "";
    [MaxLength(300)] public string Title { get; set; } = "";

    public SessionKind Kind { get; set; } = SessionKind.Lecture;

    [MaxLength(200)] public string? SpeakerName { get; set; }
    [MaxLength(200)] public string? SpeakerTitle { get; set; }
    [MaxLength(200)] public string? SpeakerOrganisation { get; set; }

    [MaxLength(2000)] public string? Abstract { get; set; }

    /// <summary>The hall it is in, when it is in one. Null for an off-floor conference suite.</summary>
    public int? HallId { get; set; }
    public Hall? Hall { get; set; }

    /// <summary>Theatre, room or suite name. The only location a visitor is actually told.</summary>
    [MaxLength(160)] public string? RoomName { get; set; }

    /// <summary>Set when an exhibitor is hosting, so their stand can be offered alongside.</summary>
    public int? ExhibitorId { get; set; }
    public Exhibitor? Exhibitor { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? SubCategoryId { get; set; }
    public Category? SubCategory { get; set; }

    public DateOnly EventDate { get; set; }
    public TimeOnly StartsAt { get; set; }
    public TimeOnly EndsAt { get; set; }

    /// <summary>Seats, where there is a limit. Zero means uncapped.</summary>
    public int Capacity { get; set; }

    /// <summary>Whether a visitor is expected to reserve a place rather than turn up.</summary>
    public bool RequiresBooking { get; set; }

    [MaxLength(8)] public string? Language { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public List<SessionBookmark> Bookmarks { get; set; } = [];

    public int DurationMinutes => (int)(EndsAt.ToTimeSpan() - StartsAt.ToTimeSpan()).TotalMinutes;
}

/// <summary>
/// A visitor saving a session to their own agenda from the mobile app.
///
/// Deliberately not called a booking: it does not hold a seat, and the app says
/// so. Turning it into a real reservation would need the organiser to manage
/// capacity and cancellations, which is a different feature with a different
/// promise attached to it.
/// </summary>
public class SessionBookmark
{
    public long Id { get; set; }

    public int VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    public int SessionId { get; set; }
    public ProgrammeSession Session { get; set; } = null!;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
