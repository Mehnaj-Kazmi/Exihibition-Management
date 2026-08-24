using System.ComponentModel.DataAnnotations;

namespace Exb.Data.Entities;

public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
}

/// <summary>Which entity a form schema shapes.</summary>
public enum FormEntity
{
    Visitor = 0,
    Exhibitor = 1,
}

/// <summary>
/// The evening e-catalogue pack for one visitor on one day: everything they
/// scanned, zipped, uploaded to a transfer service, and linked from an email.
/// </summary>
public class DeliveryJob
{
    public int Id { get; set; }

    public int VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    public DateOnly EventDate { get; set; }

    [MaxLength(400)] public string? ZipPath { get; set; }
    public long ZipSizeBytes { get; set; }
    public int ItemCount { get; set; }

    /// <summary>
    /// Unguessable token the pack is downloaded with. It is the only credential
    /// on that link, so it is long and random rather than derived from the
    /// visitor or job id, and the link expires.
    /// </summary>
    [MaxLength(48)] public string DownloadToken { get; set; } = "";

    [MaxLength(32)] public string? TransferProvider { get; set; }
    [MaxLength(1000)] public string? TransferUrl { get; set; }
    public DateTime? TransferExpiresUtc { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;
    [MaxLength(2000)] public string? Error { get; set; }
    public int Attempts { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Set when the covering email has been handed to the mail sender.</summary>
    public long? OutboxEmailId { get; set; }
}

/// <summary>
/// The end-of-day interest report for one visitor: where they showed interest,
/// and — the part exhibitors actually pay for — which stands in those same
/// categories they never reached.
/// </summary>
public class DailyReport
{
    public int Id { get; set; }

    public int VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    public DateOnly EventDate { get; set; }

    /// <summary>Rendered report, stored so a resend is byte-identical to what was sent.</summary>
    public string Html { get; set; } = "";

    /// <summary>Interest rollup, as JSON, for the visitor's own page and for analytics.</summary>
    public string InterestJson { get; set; } = "[]";

    /// <summary>The missed-stand table, as JSON.</summary>
    public string MissedJson { get; set; } = "[]";

    public int StandsVisited { get; set; }
    public int StandsMissed { get; set; }
    public int TotalDwellSeconds { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public long? OutboxEmailId { get; set; }
}

/// <summary>
/// Outbound mail queue. Every message is written here first and sent from here,
/// so that with no SMTP server configured the system still works end to end and
/// an admin can read exactly what would have gone out.
/// </summary>
public class OutboxEmail
{
    public long Id { get; set; }

    [MaxLength(320)] public string ToAddress { get; set; } = "";
    [MaxLength(200)] public string? ToName { get; set; }
    [MaxLength(400)] public string Subject { get; set; } = "";

    public string HtmlBody { get; set; } = "";
    public string? TextBody { get; set; }

    /// <summary>Absolute paths of files to attach, as a JSON array. Usually empty: packs go by link.</summary>
    public string AttachmentsJson { get; set; } = "[]";

    [MaxLength(32)] public string Kind { get; set; } = "general";

    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    [MaxLength(2000)] public string? Error { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentUtc { get; set; }
}

/// <summary>
/// A versioned, admin-arranged form definition. Each exhibition can have its
/// own layout of the visitor and exhibitor forms; the schema JSON holds the
/// sections, fields and ordering, and only one schema per entity is active.
/// </summary>
public class FormSchema
{
    public int Id { get; set; }

    public FormEntity Entity { get; set; }
    [MaxLength(160)] public string Name { get; set; } = "";
    public int Version { get; set; } = 1;

    public string SchemaJson { get; set; } = "{}";

    public bool IsActive { get; set; }
    [MaxLength(400)] public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Singleton-style key/value store for the settings screens.</summary>
public class SettingEntry
{
    [MaxLength(80)] public string Key { get; set; } = "";
    public string ValueJson { get; set; } = "{}";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(120)] public string? UpdatedBy { get; set; }
}

/// <summary>
/// Network location of a physical reader. The facility model derives which
/// antennas hang off which reader; this is the only part that cannot be
/// derived, because it depends on how the site was cabled and addressed.
/// </summary>
public class ReaderEndpoint
{
    public int Id { get; set; }

    [MaxLength(40)] public string ReaderCode { get; set; } = "";
    [MaxLength(120)] public string Host { get; set; } = "";
    public int Port { get; set; } = 5084;
    [MaxLength(80)] public string? Model { get; set; }
    public bool IsEnabled { get; set; } = true;

    [MaxLength(400)] public string? Notes { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Last known position of every badge, snapshotted periodically so a restart
/// does not lose the floor picture and so a lost-badge lookup can still say
/// where it was last seen.
/// </summary>
public class TagPositionSnapshot
{
    [MaxLength(64)] public string Epc { get; set; } = "";

    public int? HallId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    [MaxLength(8)] public string? Zone { get; set; }
    public int? KioskId { get; set; }

    public double Confidence { get; set; }
    public double UncertaintyM { get; set; }
    public double BestRssi { get; set; }
    public int AntennaCount { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public long ReadCount { get; set; }
}

/// <summary>Who changed what. Settings changes in particular need an owner.</summary>
public class AuditEntry
{
    public long Id { get; set; }

    public DateTime Utc { get; set; } = DateTime.UtcNow;
    [MaxLength(120)] public string? User { get; set; }
    [MaxLength(80)] public string Action { get; set; } = "";
    [MaxLength(80)] public string? EntityName { get; set; }
    [MaxLength(40)] public string? EntityId { get; set; }
    public string? DetailJson { get; set; }
}
