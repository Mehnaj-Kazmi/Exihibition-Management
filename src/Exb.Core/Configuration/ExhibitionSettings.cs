namespace Exb.Core.Configuration;

/// <summary>Identity of the event itself, used on badges, QR landing pages and reports.</summary>
public sealed class ExhibitionSettings
{
    public string Name { get; set; } = "SMA Tech Exhibition";
    public string Edition { get; set; } = "";
    public string Venue { get; set; } = "";
    public string OrganiserName { get; set; } = "SMA Technology";
    public string OrganiserEmail { get; set; } = "info@example.com";
    public string? LogoPath { get; set; }

    /// <summary>Absolute base URL a scanned QR code resolves to. Must be reachable from visitors' phones.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>IANA or Windows time zone id used to decide when "today" ends.</summary>
    public string TimeZoneId { get; set; } = "Arabia Standard Time";

    /// <summary>Local time at which end-of-day processing runs.</summary>
    public TimeOnly EndOfDayAt { get; set; } = new(19, 0);

    /// <summary>Run end-of-day packs and reports automatically, rather than only on the button.</summary>
    public bool AutoRunEndOfDay { get; set; } = true;

    public ExhibitionSettings Clone() => (ExhibitionSettings)MemberwiseClone();
}

/// <summary>
/// The thresholds that turn standing still into interest.
///
/// These are the most consequential numbers in the product: they decide what an
/// exhibitor is told about a visitor, so they are admin-editable and the report
/// states which values produced it.
/// </summary>
public sealed class DwellSettings
{
    /// <summary>
    /// How far outside a stand's footprint a badge still counts as being at it.
    /// Visitors stand at the edge of a stand, not inside it, and position
    /// uncertainty is around a metre, so zero here would lose most real visits.
    /// </summary>
    public double AttachRadiusM { get; set; } = 2.0;

    /// <summary>Ignore fixes less certain than this; a wild fix should not open a visit.</summary>
    public double MinConfidence { get; set; } = 0.25;

    /// <summary>Below this a stop is recorded as a pass-by and never called interest.</summary>
    public int MinDwellSeconds { get; set; } = 20;

    /// <summary>At or above this the stop is reported to the visitor as an interest.</summary>
    public int InterestSeconds { get; set; } = 45;

    /// <summary>At or above this it was a demo or a conversation.</summary>
    public int StrongSeconds { get; set; } = 180;

    /// <summary>A gap in attribution longer than this closes the session.</summary>
    public int BreakSeconds { get; set; } = 30;

    /// <summary>
    /// Cap on a single session. A badge left on a stand's counter overnight is
    /// not an eight-hour conversation, and without a cap it would dominate every
    /// category rollup in the building.
    /// </summary>
    public int MaxSessionSeconds { get; set; } = 3600;

    /// <summary>
    /// A visit is only counted as interest if this stand beat the runner-up by
    /// this margin, in metres, on average. On a tight row of 3 m shells the
    /// honest answer is often "one of these two", and this is what stops the
    /// system inventing a winner.
    /// </summary>
    public double MinMarginM { get; set; } = 0.35;

    public DwellSettings Clone() => (DwellSettings)MemberwiseClone();
}

/// <summary>How the evening e-catalogue pack reaches the visitor.</summary>
public sealed class DeliverySettings
{
    /// <summary>"local", "wetransfer" or "generic".</summary>
    public string Provider { get; set; } = "local";

    /// <summary>Days a download link stays alive.</summary>
    public int LinkExpiryDays { get; set; } = 7;

    /// <summary>Where built zips and uploaded catalogues live on disk.</summary>
    public string StorageRoot { get; set; } = "App_Data";

    /// <summary>Largest pack we will build before splitting is needed.</summary>
    public int MaxPackMegabytes { get; set; } = 400;

    /// <summary>
    /// Attach the pack to the email instead of linking it, when it is small
    /// enough. Most mail servers reject anything over about 20 MB.
    /// </summary>
    public bool AttachIfUnderMegabytes { get; set; } = false;
    public int AttachThresholdMegabytes { get; set; } = 8;

    public WeTransferSettings WeTransfer { get; set; } = new();
    public GenericTransferSettings Generic { get; set; } = new();

    public DeliverySettings Clone() => new()
    {
        Provider = Provider,
        LinkExpiryDays = LinkExpiryDays,
        StorageRoot = StorageRoot,
        MaxPackMegabytes = MaxPackMegabytes,
        AttachIfUnderMegabytes = AttachIfUnderMegabytes,
        AttachThresholdMegabytes = AttachThresholdMegabytes,
        WeTransfer = WeTransfer.Clone(),
        Generic = Generic.Clone(),
    };
}

public sealed class WeTransferSettings
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://dev.wetransfer.com/v2";
    public string Message { get; set; } = "Your exhibition e-catalogue pack";
    public WeTransferSettings Clone() => (WeTransferSettings)MemberwiseClone();
}

/// <summary>
/// Any transfer service that accepts a multipart POST and answers with a URL.
/// Covers Filemail, Smash, an in-house file drop, or a corporate gateway.
/// </summary>
public sealed class GenericTransferSettings
{
    public string UploadUrl { get; set; } = "";
    public string FileFieldName { get; set; } = "file";
    public string? AuthorizationHeader { get; set; }

    /// <summary>Dotted path to the download URL inside the JSON response, e.g. "data.url".</summary>
    public string UrlJsonPath { get; set; } = "url";

    public GenericTransferSettings Clone() => (GenericTransferSettings)MemberwiseClone();
}

/// <summary>
/// Mail transport. The default deliberately sends nothing: messages queue in the
/// database Outbox where an admin can read them, and only switching the provider
/// to "smtp" with a configured server starts real delivery to real people.
/// </summary>
public sealed class MailSettings
{
    /// <summary>"outbox" (queue only) or "smtp".</summary>
    public string Provider { get; set; } = "outbox";

    public string FromAddress { get; set; } = "no-reply@example.com";
    public string FromName { get; set; } = "SMA Tech Exhibition";
    public string? ReplyTo { get; set; }

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public bool UseSsl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// Send every generated message to this address instead of the visitor's.
    /// The safety catch for rehearsing a real event with real registration data.
    /// </summary>
    public string? RedirectAllTo { get; set; }

    public int MaxSendsPerMinute { get; set; } = 60;

    public MailSettings Clone() => (MailSettings)MemberwiseClone();
}

/// <summary>The synthetic visitor population used when no readers are connected.</summary>
public sealed class SimulatorSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Badges on the floor. Registered visitors are used first, then extras are invented.</summary>
    public int VisitorCount { get; set; } = 120;

    public double WalkSpeedMps { get; set; } = 1.1;

    /// <summary>Chance a read is lost to tag orientation, RF nulls or collisions.</summary>
    public double DropoutProbability { get; set; } = 0.06;

    public double RssiNoiseDb { get; set; } = 2.5;

    /// <summary>Fraction of stand visits where the visitor also scans the QR code.</summary>
    public double ScanProbability { get; set; } = 0.35;

    /// <summary>
    /// Multiplies how long simulated visitors linger at a stand. Walking speed
    /// is left alone deliberately: compressing movement too would thin out the
    /// reads a badge collects while crossing a stand, and the locating accuracy
    /// measured in a rehearsal would then be better than the real thing.
    /// </summary>
    public double DwellScale { get; set; } = 1.0;

    public int Seed { get; set; } = 20260817;

    public SimulatorSettings Clone() => (SimulatorSettings)MemberwiseClone();
}
