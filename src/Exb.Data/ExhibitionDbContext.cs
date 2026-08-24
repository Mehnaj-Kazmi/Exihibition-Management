using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data;

/// <summary>
/// The exhibition database.
///
/// Two conventions run through the model. First, history is protected: visits,
/// scans, packs and reports are what the organiser sells and what a visitor was
/// promised, so deletes are restricted and things are retired with IsActive
/// rather than removed. Second, the columns the system reasons about are real
/// columns, and only the organiser's own per-exhibition questions live in JSON.
/// </summary>
public class ExhibitionDbContext(DbContextOptions<ExhibitionDbContext> options) : DbContext(options)
{
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Exhibitor> Exhibitors => Set<Exhibitor>();
    public DbSet<Kiosk> Kiosks => Set<Kiosk>();
    public DbSet<CatalogueAsset> CatalogueAssets => Set<CatalogueAsset>();
    public DbSet<EventDay> EventDays => Set<EventDay>();

    public DbSet<ProgrammeSession> Sessions => Set<ProgrammeSession>();

    public DbSet<Visitor> Visitors => Set<Visitor>();
    public DbSet<VisitorVisit> Visits => Set<VisitorVisit>();
    public DbSet<CatalogueRequest> CatalogueRequests => Set<CatalogueRequest>();
    public DbSet<SessionBookmark> SessionBookmarks => Set<SessionBookmark>();

    public DbSet<VisitorLoginCode> VisitorLoginCodes => Set<VisitorLoginCode>();
    public DbSet<MobileSession> MobileSessions => Set<MobileSession>();

    public DbSet<DeliveryJob> DeliveryJobs => Set<DeliveryJob>();
    public DbSet<DailyReport> DailyReports => Set<DailyReport>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();

    public DbSet<FormSchema> FormSchemas => Set<FormSchema>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();
    public DbSet<ReaderEndpoint> ReaderEndpoints => Set<ReaderEndpoint>();
    public DbSet<TagPositionSnapshot> TagPositions => Set<TagPositionSnapshot>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // --- exhibition layout ------------------------------------------------

        b.Entity<Hall>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).IsRequired();
            e.Property(x => x.Name).IsRequired();
        });

        b.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Parent)
             .WithMany(x => x.Children)
             .HasForeignKey(x => x.ParentId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Exhibitor>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.CompanyName);
            e.HasOne(x => x.Category)
             .WithMany(c => c.Exhibitors)
             .HasForeignKey(x => x.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SubCategory)
             .WithMany()
             .HasForeignKey(x => x.SubCategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Kiosk>(e =>
        {
            e.HasIndex(x => new { x.HallId, x.StandNumber }).IsUnique();
            e.HasIndex(x => x.QrToken).IsUnique();
            e.HasIndex(x => x.ExhibitorId);
            e.HasOne(x => x.Exhibitor)
             .WithMany(x => x.Kiosks)
             .HasForeignKey(x => x.ExhibitorId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Hall)
             .WithMany(x => x.Kiosks)
             .HasForeignKey(x => x.HallId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CatalogueAsset>(e =>
        {
            e.HasIndex(x => x.ExhibitorId);
            e.HasOne(x => x.Exhibitor)
             .WithMany(x => x.Catalogues)
             .HasForeignKey(x => x.ExhibitorId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EventDay>(e => e.HasIndex(x => x.Date).IsUnique());

        // --- the programme ----------------------------------------------------

        b.Entity<ProgrammeSession>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            // The app's default screen is "today, in time order", and its filters
            // narrow from there, so the day leads every index on this table.
            e.HasIndex(x => new { x.EventDate, x.StartsAt });
            e.HasIndex(x => new { x.EventDate, x.Kind });
            e.HasIndex(x => new { x.EventDate, x.HallId });
            e.HasIndex(x => x.CategoryId);
            e.Property(x => x.Title).IsRequired();

            // Sessions are retired with IsActive, but a hall or category that no
            // longer exists should not take a published programme down with it.
            e.HasOne(x => x.Hall).WithMany().HasForeignKey(x => x.HallId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Exhibitor).WithMany().HasForeignKey(x => x.ExhibitorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SubCategory).WithMany().HasForeignKey(x => x.SubCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        // --- people and behaviour --------------------------------------------

        b.Entity<Visitor>(e =>
        {
            // Filtered so that pre-registered visitors without a badge yet do not
            // all collide on the empty string.
            e.HasIndex(x => x.BadgeEpc).IsUnique().HasFilter("[BadgeEpc] <> ''");
            e.HasIndex(x => x.RegistrationCode).IsUnique();
            e.HasIndex(x => x.AccessToken).IsUnique();
            e.HasIndex(x => x.Email);
            e.Property(x => x.FullName).IsRequired();
        });

        b.Entity<VisitorVisit>(e =>
        {
            e.HasIndex(x => new { x.VisitorId, x.EventDate });
            e.HasIndex(x => new { x.KioskId, x.EventDate });
            e.HasIndex(x => new { x.EventDate, x.Level });
            e.HasIndex(x => new { x.CategoryId, x.EventDate });
            // The dwell engine reopens in-flight sessions after a restart; this
            // keeps that lookup off a full scan of the day's visits.
            e.HasIndex(x => x.IsOpen).HasFilter("[IsOpen] = 1");
            e.HasOne(x => x.Visitor)
             .WithMany(x => x.Visits)
             .HasForeignKey(x => x.VisitorId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Kiosk)
             .WithMany(x => x.Visits)
             .HasForeignKey(x => x.KioskId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CatalogueRequest>(e =>
        {
            // One row per visitor per stand per day: scanning the same QR twice
            // is the same request, not two catalogues in the pack.
            e.HasIndex(x => new { x.VisitorId, x.KioskId, x.EventDate }).IsUnique();
            e.HasIndex(x => new { x.EventDate, x.VisitorId });
            e.HasOne(x => x.Visitor)
             .WithMany(x => x.CatalogueRequests)
             .HasForeignKey(x => x.VisitorId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Kiosk)
             .WithMany(x => x.CatalogueRequests)
             .HasForeignKey(x => x.KioskId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SessionBookmark>(e =>
        {
            // Saving the same talk twice is the same agenda entry, not two.
            e.HasIndex(x => new { x.VisitorId, x.SessionId }).IsUnique();
            e.HasOne(x => x.Visitor).WithMany().HasForeignKey(x => x.VisitorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Session)
             .WithMany(s => s.Bookmarks)
             .HasForeignKey(x => x.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // --- mobile app access ------------------------------------------------

        b.Entity<VisitorLoginCode>(e =>
        {
            // Verification looks a code up by visitor and recency; nothing ever
            // searches by the hash, so that is not the index.
            e.HasIndex(x => new { x.VisitorId, x.CreatedUtc });
            e.HasIndex(x => x.ExpiresUtc);
            e.HasOne(x => x.Visitor).WithMany().HasForeignKey(x => x.VisitorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MobileSession>(e =>
        {
            // Every authenticated request is this lookup, so it has to be unique
            // and indexed rather than a scan of the signed-in device list.
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.VisitorId);
            e.HasOne(x => x.Visitor).WithMany().HasForeignKey(x => x.VisitorId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- operations -------------------------------------------------------

        b.Entity<DeliveryJob>(e =>
        {
            e.HasIndex(x => new { x.VisitorId, x.EventDate }).IsUnique();
            e.HasIndex(x => new { x.EventDate, x.Status });
            e.HasIndex(x => x.DownloadToken).IsUnique().HasFilter("[DownloadToken] <> ''");
            e.HasOne(x => x.Visitor)
             .WithMany()
             .HasForeignKey(x => x.VisitorId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DailyReport>(e =>
        {
            e.HasIndex(x => new { x.VisitorId, x.EventDate }).IsUnique();
            e.HasIndex(x => new { x.EventDate, x.Status });
            e.HasOne(x => x.Visitor)
             .WithMany()
             .HasForeignKey(x => x.VisitorId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OutboxEmail>(e => e.HasIndex(x => new { x.Status, x.CreatedUtc }));

        b.Entity<FormSchema>(e =>
        {
            e.HasIndex(x => new { x.Entity, x.Name, x.Version }).IsUnique();
            // Exactly one live layout per form, enforced by the database rather
            // than by hoping the application always remembers to deactivate.
            e.HasIndex(x => x.Entity).IsUnique().HasFilter("[IsActive] = 1");
        });

        b.Entity<SettingEntry>(e => e.HasKey(x => x.Key));

        b.Entity<ReaderEndpoint>(e => e.HasIndex(x => x.ReaderCode).IsUnique());

        b.Entity<TagPositionSnapshot>(e =>
        {
            e.HasKey(x => x.Epc);
            e.HasIndex(x => x.LastSeenUtc);
        });

        b.Entity<AuditEntry>(e => e.HasIndex(x => x.Utc));

        base.OnModelCreating(b);
    }
}
