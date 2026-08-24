namespace Exb.Mobile.Shared.Models;

public sealed class Visitor
{
    public int VisitorId { get; set; }
    public string FullName { get; set; } = "Visitor";
    public string Email { get; set; } = "";
    public string RegistrationCode { get; set; } = "";
    public string? Company { get; set; }
    public string? JobTitle { get; set; }
    public string? Country { get; set; }
    public bool ConsentEmail { get; set; }
    public bool ConsentTracking { get; set; }
    public bool HasBadge { get; set; }
}

public sealed class Exhibition
{
    public string Name { get; set; } = "Exhibition";
    public string? Edition { get; set; }
    public string? Venue { get; set; }
    public string? Organiser { get; set; }
    public string? OrganiserEmail { get; set; }
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public List<Hall> Halls { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public List<DateOnly> ProgrammeDates { get; set; } = [];
}

/// <summary>Wire shape matches the server's HallSummary record.</summary>
public sealed class Hall
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public double WidthM { get; set; }
    public double DepthM { get; set; }
    public int StandCount { get; set; }
    public int ExhibitorCount { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Wire shape matches the server's HallDetail record: {summary, exhibitors, sessionCount}.</summary>
public sealed class HallDetail
{
    public Hall Summary { get; set; } = new();
    public List<Exhibitor> Exhibitors { get; set; } = [];
    public int SessionCount { get; set; }
}

/// <summary>Two-level taxonomy node; top-level rows carry Children, sub-category rows have an empty list.</summary>
public sealed class Category
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Colour { get; set; }
    public string? Description { get; set; }
    public int ExhibitorCount { get; set; }
    public List<Category> Children { get; set; } = [];
}

public sealed class Stand
{
    public int KioskId { get; set; }
    public string StandNumber { get; set; } = "";
    public int HallId { get; set; }
    public string HallCode { get; set; } = "";
    public string HallName { get; set; } = "";
}

/// <summary>Wire shape matches the server's ExhibitorSummary record.</summary>
public sealed class Exhibitor
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? Country { get; set; }
    public string? Summary { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? SubCategoryId { get; set; }
    public string? SubCategoryName { get; set; }
    public List<Stand> Stands { get; set; } = [];
    public int CatalogueCount { get; set; }

    public string Location => Stands.Count == 0
        ? "Stand not yet allocated"
        : $"{string.Join(", ", Stands.Select(s => s.HallName).Distinct())} · {string.Join(", ", Stands.Select(s => s.StandNumber))}";
}

/// <summary>Wire shape matches the server's ExhibitorDetail record: {summary, contactName, email, phone, website, sessions, catalogueRequested}.</summary>
public sealed class ExhibitorDetail
{
    public Exhibitor Summary { get; set; } = new();
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public List<Session> Sessions { get; set; } = [];
    public bool CatalogueRequested { get; set; }
}

/// <summary>Wire shape matches the server's SessionSummary record (StartsAt/EndsAt are TimeOnly).</summary>
public sealed class Session
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "Lecture";
    public string? SpeakerName { get; set; }
    public string? SpeakerTitle { get; set; }
    public string? SpeakerOrganisation { get; set; }
    public DateOnly EventDate { get; set; }
    public TimeOnly StartsAt { get; set; }
    public TimeOnly EndsAt { get; set; }
    public int? HallId { get; set; }
    public string? HallName { get; set; }
    public string? RoomName { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? SubCategoryName { get; set; }
    public int? ExhibitorId { get; set; }
    public string? ExhibitorName { get; set; }
    public bool RequiresBooking { get; set; }
    public int Capacity { get; set; }
    public string? Language { get; set; }
    public bool Bookmarked { get; set; }

    public int StartsAtMinutes => StartsAt.Hour * 60 + StartsAt.Minute;
    public int EndsAtMinutes => EndsAt.Hour * 60 + EndsAt.Minute;
    public string TimeRange => $"{StartsAt:HH:mm}–{EndsAt:HH:mm}";
    public int DurationMinutes => EndsAtMinutes - StartsAtMinutes;
    public string Where => RoomName is not null && HallName is not null
        ? $"{RoomName} · {HallName}"
        : RoomName ?? HallName ?? "Location to be confirmed";

    public Session WithBookmarked(bool bookmarked) => new()
    {
        Id = Id, Code = Code, Title = Title, Kind = Kind, SpeakerName = SpeakerName,
        SpeakerTitle = SpeakerTitle, SpeakerOrganisation = SpeakerOrganisation, EventDate = EventDate,
        StartsAt = StartsAt, EndsAt = EndsAt, HallId = HallId, HallName = HallName,
        RoomName = RoomName, CategoryId = CategoryId, CategoryName = CategoryName, SubCategoryName = SubCategoryName,
        ExhibitorId = ExhibitorId, ExhibitorName = ExhibitorName, RequiresBooking = RequiresBooking,
        Capacity = Capacity, Language = Language, Bookmarked = bookmarked,
    };
}

/// <summary>Wire shape matches the server's SessionDetail record: {summary, abstract}.</summary>
public sealed class SessionDetail
{
    public Session Summary { get; set; } = new();
    public string? Abstract { get; set; }
}

public sealed class ScannedStand
{
    public int KioskId { get; set; }
    public string StandNumber { get; set; } = "";
    public int ExhibitorId { get; set; }
    public string ExhibitorName { get; set; } = "";
    public string HallName { get; set; } = "";
    public string? CategoryName { get; set; }
    public string? SubCategoryName { get; set; }
    public string? Summary { get; set; }
    public string? Website { get; set; }
    public int CatalogueFileCount { get; set; }
}

/// <summary>Wire shape matches the server's scan/catalogue-request response: {outcome: "added"|"alreadyRequested", message, stand, todayCount}.</summary>
public sealed class ScanResult
{
    public string Outcome { get; set; } = "added";
    public string Message { get; set; } = "Added to your list.";
    public ScannedStand Stand { get; set; } = new();
    public int TodayCount { get; set; }

    public bool IsAlreadyRequested => Outcome == "alreadyRequested";
}

public sealed class VisitedStand
{
    public int ExhibitorId { get; set; }
    public string ExhibitorName { get; set; } = "";
    public string Location { get; set; } = "";
    public string? CategoryName { get; set; }
    public string DwellText { get; set; } = "";
    public string LevelText { get; set; } = "";
    public bool CatalogueRequested { get; set; }
}

public sealed class CategoryInterest
{
    public string CategoryName { get; set; } = "";
    public string DwellText { get; set; } = "";
    public int StandCount { get; set; }
    public double SharePct { get; set; }
}

public sealed class MissedStand
{
    public int ExhibitorId { get; set; }
    public string ExhibitorName { get; set; } = "";
    public string Location { get; set; } = "";
    public string? CategoryName { get; set; }
    public string? Website { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>
/// The clean, consumer-facing shape the UI binds to. The wire response is a
/// discriminated union ({trackingConsent:false, message} vs
/// {trackingConsent:true, day:{...}}) — <see cref="Exb.Mobile.Shared.Services.ApiClient"/>
/// unwraps that on the way in, exactly like the original Flutter client did.
/// </summary>
public sealed class VisitorDay
{
    public bool TrackingConsent { get; set; }
    public string? Message { get; set; }
    public string? TotalDwellText { get; set; }
    public int? StandsWithInterest { get; set; }
    public List<VisitedStand> Visited { get; set; } = [];
    public List<CategoryInterest> Categories { get; set; } = [];
    public List<MissedStand> Missed { get; set; } = [];
}

/// <summary>Wire shape matches the server's Page&lt;T&gt; record: {items, total, pageNumber, pageSize, hasMore}.</summary>
public sealed class Paged<T>
{
    public List<T> Items { get; set; } = [];
    public int Total { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public bool HasMore { get; set; }

    public static Paged<T> Empty() => new();
}

/// <summary>Wire shape matches the server's UnifiedSearchResult record.</summary>
public sealed class SearchResults
{
    public List<Exhibitor> Exhibitors { get; set; } = [];
    public List<Session> Sessions { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<Hall> Halls { get; set; } = [];
    public int ExhibitorTotal { get; set; }
    public int SessionTotal { get; set; }

    public bool IsEmpty => Exhibitors.Count == 0 && Sessions.Count == 0 && Categories.Count == 0 && Halls.Count == 0;
}

public sealed class LoginCodeRequest
{
    public string Message { get; set; } = "Check your email for the code.";
    public int ExpiresInSeconds { get; set; } = 900;

    /// <summary>Present only when the server isn't actually sending email (dev/test mode) — shown directly in the UI.</summary>
    public string? DevelopmentCode { get; set; }
}
