using System.ComponentModel.DataAnnotations;

namespace Exb.Data.Entities;

/// <summary>
/// A one-time code emailed to a visitor's registered address so they can sign in
/// to the mobile app.
///
/// Only a hash of the code is stored. It is a six-digit number that unlocks
/// somebody's leads and contact details, and a database dump — or an admin
/// reading the table — should not hand that over. The same reasoning is why the
/// attempt count lives here rather than in memory: the limit has to survive an
/// app restart, otherwise it is not a limit.
/// </summary>
public class VisitorLoginCode
{
    public long Id { get; set; }

    public int VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    /// <summary>Lower-cased address the code was sent to, kept for the audit trail.</summary>
    [MaxLength(320)] public string EmailSentTo { get; set; } = "";

    /// <summary>SHA-256 of the code, hex encoded. The code itself is never stored.</summary>
    [MaxLength(64)] public string CodeHash { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }

    /// <summary>Set the moment the code is exchanged for a session, so it cannot be replayed.</summary>
    public DateTime? ConsumedUtc { get; set; }

    /// <summary>Wrong guesses so far. The code dies after a handful.</summary>
    public int Attempts { get; set; }

    [MaxLength(64)] public string? RequestedFromIp { get; set; }

    public long? OutboxEmailId { get; set; }
}

/// <summary>
/// A signed-in mobile device.
///
/// The token is stored hashed for the same reason as the login code, and each
/// device gets its own row so a visitor who loses a phone can be signed out of
/// that one without ending the session on the tablet they left at the office.
/// </summary>
public class MobileSession
{
    public long Id { get; set; }

    public int VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    /// <summary>SHA-256 of the bearer token, hex encoded.</summary>
    [MaxLength(64)] public string TokenHash { get; set; } = "";

    /// <summary>"android" or "ios", as reported by the app. Informational only.</summary>
    [MaxLength(32)] public string? Platform { get; set; }

    /// <summary>Device name shown on the visitor's own sessions list.</summary>
    [MaxLength(120)] public string? DeviceName { get; set; }

    [MaxLength(40)] public string? AppVersion { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Long enough to cover a multi-day show without a visitor being signed out
    /// mid-aisle, short enough that a forgotten phone does not stay signed in
    /// for a year.
    /// </summary>
    public DateTime ExpiresUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }

    public bool IsUsable(DateTime utcNow) => RevokedUtc is null && ExpiresUtc > utcNow;
}
