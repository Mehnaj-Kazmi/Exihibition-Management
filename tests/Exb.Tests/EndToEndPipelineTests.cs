using Exb.Core.Configuration;
using Exb.Core.Delivery;
using Exb.Core.Dwell;
using Exb.Core.Mail;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Exb.Tests;

/// <summary>
/// The whole evening, end to end: a visitor walks the floor, scans a stand, the
/// halls close, and a pack and a report come out with an email queued behind
/// them.
///
/// It runs against EF Core's in-memory provider rather than SQL Server, so it
/// exercises the real services and the real entity graph without needing a
/// database server. What it deliberately does not prove is anything
/// SQL-Server-specific — the filtered unique indexes in particular are enforced
/// by the database, not by this provider, and are covered by deploying the
/// migration.
/// </summary>
public class EndToEndPipelineTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "exb-e2e-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly InMemoryContextFactory _factory = new();
    private static readonly DateOnly Day = new(2026, 8, 17);

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class InMemoryContextFactory : IDbContextFactory<ExhibitionDbContext>
    {
        private readonly string _name = Guid.NewGuid().ToString();

        public ExhibitionDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<ExhibitionDbContext>()
                .UseInMemoryDatabase(_name)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class LocalOnlySelector(ExhibitionSettings exhibition) : ITransferProviderSelector
    {
        public ITransferProvider Resolve(AppSettings settings)
            => new LocalLinkTransferProvider(exhibition, settings.Delivery);
    }

    [Fact]
    public async Task AVisitorsDayBecomesAPackAReportAndAQueuedEmail()
    {
        // --- an exhibition with three stands in two categories ---------------
        await using (var db = _factory.CreateDbContext())
        {
            var hall = new Hall { Code = "H1", Name = "Hall 1", WidthM = 40, DepthM = 30 };
            var textiles = new Category { Code = "TEX", Name = "Textile Machinery" };
            var weaving = new Category { Code = "TEX-1", Name = "Weaving", Parent = textiles };
            var packaging = new Category { Code = "PKG", Name = "Packaging" };

            db.AddRange(hall, textiles, weaving, packaging);

            var visited = NewExhibitor("Meridian Looms", textiles, weaving, hall, "H1-001", 3, 3);
            var alsoVisited = NewExhibitor("Bluepeak Cartons", packaging, null, hall, "H1-002", 12, 3);
            var missed = NewExhibitor("Nordwind Weaving", textiles, weaving, hall, "H1-003", 21, 3);
            var walkedPast = NewExhibitor("Ironline Looms", textiles, weaving, hall, "H1-004", 30, 3);

            db.AddRange(
                visited.Exhibitor, visited.Kiosk,
                alsoVisited.Exhibitor, alsoVisited.Kiosk,
                missed.Exhibitor, missed.Kiosk,
                walkedPast.Exhibitor, walkedPast.Kiosk);

            db.Visitors.Add(new Visitor
            {
                Id = 1,
                FullName = "Sara Khan",
                Email = "sara@visitor.example",
                BadgeEpc = "3034257BF4A1B2C3D4E5F607",
                RegistrationCode = "AAAA-BBBB",
                AccessToken = "TOKENSARA",
                ConsentEmail = true,
                ConsentTracking = true,
            });

            await db.SaveChangesAsync();
        }

        // --- what the floor recorded during the day --------------------------
        await using (var db = _factory.CreateDbContext())
        {
            var kiosks = await db.Kiosks.OrderBy(k => k.StandNumber).ToListAsync();

            db.Visits.AddRange(
                Visit(1, kiosks[0], 420, InterestLevel.Strong),      // H1-001, a real stop
                Visit(1, kiosks[1], 50, InterestLevel.Interested),   // H1-002, a shorter one
                // H1-004: walked past without stopping. Not interest, and — the
                // point of the assertion further down — not something to tell
                // them they "missed" either, since they were standing there.
                Visit(1, kiosks[3], 6, InterestLevel.PassBy));

            db.CatalogueRequests.Add(new CatalogueRequest
            {
                VisitorId = 1,
                KioskId = kiosks[0].Id,
                ExhibitorId = kiosks[0].ExhibitorId,
                EventDate = Day,
                Source = "qr",
                Included = true,
            });

            await db.SaveChangesAsync();
        }

        // --- the evening run -------------------------------------------------
        var settings = new SettingsStore(_factory);
        await settings.EnsureDefaultsAsync();
        await settings.SaveAsync(SettingsKeys.Exhibition, new ExhibitionSettings
        {
            Name = "SMA Tech Expo",
            PublicBaseUrl = "https://expo.example.com",
        }, "test");

        var facility = new FacilityProvider(_factory, settings, NullLogger<FacilityProvider>.Instance);
        await facility.RebuildAsync();

        var interest = new InterestQueryService(_factory, facility);
        var visits = new VisitRepository(_factory);
        var storage = new CatalogueStorage(_workspace);

        var endOfDay = new EndOfDayService(
            _factory, settings, interest, visits, storage,
            new LocalOnlySelector(settings.Current.Exhibition),
            NullLogger<EndOfDayService>.Instance);

        var result = await endOfDay.RunAsync(Day);

        output.WriteLine($"visitors {result.VisitorsConsidered}, packs {result.PacksBuilt}, "
            + $"reports {result.ReportsBuilt}, emails {result.EmailsQueued}");
        foreach (string problem in result.Problems) output.WriteLine("problem: " + problem);

        Assert.True(result.Succeeded, string.Join("; ", result.Problems));
        Assert.Equal(1, result.VisitorsConsidered);
        Assert.Equal(1, result.PacksBuilt);
        Assert.Equal(1, result.ReportsBuilt);
        Assert.Equal(1, result.EmailsQueued);

        // --- what the visitor actually gets ----------------------------------
        await using (var db = _factory.CreateDbContext())
        {
            var job = await db.DeliveryJobs.SingleAsync();
            Assert.Equal(JobStatus.Succeeded, job.Status);
            Assert.Equal(1, job.ItemCount);
            Assert.StartsWith("https://expo.example.com/d/", job.TransferUrl);
            Assert.True(File.Exists(storage.ResolveStored(job.ZipPath!)), "the pack file is not on disk");

            var report = await db.DailyReports.SingleAsync();
            Assert.Equal(2, report.StandsVisited);          // the pass-by is not a visit
            Assert.Equal(470, report.TotalDwellSeconds);

            // The missed stand is in the same sub-category as where they spent
            // most of their time, so it must be recommended.
            Assert.Contains("Nordwind Weaving", report.MissedJson);

            // A stand they actually stopped at is obviously not "missed"...
            Assert.DoesNotContain("Meridian Looms", report.MissedJson);

            // ...and neither is one they walked past. Telling someone they missed
            // a stand they were physically standing in front of reads as the
            // system not knowing what it is talking about.
            Assert.DoesNotContain("Ironline Looms", report.MissedJson);

            Assert.Contains("Meridian Looms", report.Html);
            Assert.Contains("Nordwind Weaving", report.Html);
            Assert.Contains(job.TransferUrl!, report.Html);

            var mail = await db.OutboxEmails.SingleAsync();
            Assert.Equal("sara@visitor.example", mail.ToAddress);
            Assert.Equal(JobStatus.Pending, mail.Status);
            Assert.Equal("daily-report", mail.Kind);

            Assert.True(await db.EventDays.AnyAsync(d => d.Date == Day && d.Closed));
        }

        // --- and running it again does not email anyone twice ----------------
        var second = await endOfDay.RunAsync(Day);
        Assert.Equal(0, second.EmailsQueued);

        await using (var db = _factory.CreateDbContext())
            Assert.Equal(1, await db.OutboxEmails.CountAsync());
    }

    [Fact]
    public async Task NothingIsSentToAVisitorWhoDidNotConsentToEmail()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var hall = new Hall { Code = "H1", Name = "Hall 1", WidthM = 30, DepthM = 20 };
            var category = new Category { Code = "TEX", Name = "Textiles" };
            db.AddRange(hall, category);

            var stand = NewExhibitor("Quiet Co", category, null, hall, "H1-001", 3, 3);
            db.AddRange(stand.Exhibitor, stand.Kiosk);

            db.Visitors.Add(new Visitor
            {
                Id = 1,
                FullName = "No Mail",
                Email = "nomail@visitor.example",
                BadgeEpc = "EPC1",
                RegistrationCode = "CCCC-DDDD",
                AccessToken = "TOKENNOMAIL",
                ConsentEmail = false,
                ConsentTracking = true,
            });
            await db.SaveChangesAsync();

            var kiosk = await db.Kiosks.SingleAsync();
            db.Visits.Add(Visit(1, kiosk, 300, InterestLevel.Strong));
            await db.SaveChangesAsync();
        }

        var settings = new SettingsStore(_factory);
        await settings.EnsureDefaultsAsync();

        var facility = new FacilityProvider(_factory, settings, NullLogger<FacilityProvider>.Instance);
        await facility.RebuildAsync();

        var endOfDay = new EndOfDayService(
            _factory, settings, new InterestQueryService(_factory, facility), new VisitRepository(_factory),
            new CatalogueStorage(_workspace), new LocalOnlySelector(settings.Current.Exhibition),
            NullLogger<EndOfDayService>.Instance);

        var result = await endOfDay.RunAsync(Day);

        Assert.Equal(0, result.EmailsQueued);
        Assert.Equal(1, result.Skipped);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(0, await db.OutboxEmails.CountAsync());

            // The report is still built, so an organiser can see it on the screen.
            var report = await db.DailyReports.SingleAsync();
            Assert.Equal(JobStatus.Skipped, report.Status);
        }
    }

    [Fact]
    public async Task TheMailQueueHoldsEverythingUntilATransportIsConfigured()
    {
        var queue = new MailQueue(_factory, NullLogger<MailQueue>.Instance);

        await queue.QueueAsync("someone@example.com", "Someone", "Test", "<p>Body</p>", "Body");

        var (sent, failed, held) = await queue.DispatchAsync(new HeldMailTransport(), new MailSettings());

        Assert.Equal(0, sent);
        Assert.Equal(0, failed);
        Assert.Equal(1, held);

        await using var db = _factory.CreateDbContext();
        var mail = await db.OutboxEmails.SingleAsync();
        Assert.Equal(JobStatus.Pending, mail.Status);
        Assert.Equal(0, mail.Attempts);   // holding is not a failed attempt
    }

    // --- fixtures ------------------------------------------------------------

    private static (Exhibitor Exhibitor, Kiosk Kiosk) NewExhibitor(
        string name, Category category, Category? sub, Hall hall, string stand, double x, double y)
    {
        var exhibitor = new Exhibitor
        {
            Code = stand,
            CompanyName = name,
            Category = category,
            SubCategory = sub,
            Website = "www.example.com",
            Summary = $"{name} summary",
        };

        var kiosk = new Kiosk
        {
            Exhibitor = exhibitor,
            Hall = hall,
            StandNumber = stand,
            X = x,
            Y = y,
            WidthM = 6,
            DepthM = 6,
            QrToken = Tokens.New(16),
        };

        return (exhibitor, kiosk);
    }

    private static VisitorVisit Visit(int visitorId, Kiosk target, int seconds, InterestLevel level)
    {
        return new VisitorVisit
        {
            VisitorId = visitorId,
            Kiosk = target,
            ExhibitorId = target.ExhibitorId,
            HallId = target.HallId,
            CategoryId = target.Exhibitor?.CategoryId,
            SubCategoryId = target.Exhibitor?.SubCategoryId,
            EventDate = Day,
            StartedUtc = new DateTime(2026, 8, 17, 11, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 8, 17, 11, 0, 0, DateTimeKind.Utc).AddSeconds(seconds),
            DwellSeconds = seconds,
            Level = level,
            SampleCount = Math.Max(1, seconds / 10),
            MeanConfidence = 0.8,
            MeanMarginM = 1.2,
            IsOpen = false,
        };
    }
}

public class SimulatorTests(ITestOutputHelper output)
{
    [Fact]
    public void ProducesReadsThatResolveToTheStandsVisitorsAreStandingAt()
    {
        var model = TestFacility.Build();
        var settings = new SimulatorSettings { VisitorCount = 40, WalkSpeedMps = 3.0, Seed = 7 };

        var badges = Enumerable.Range(1, 40)
            .Select(i => new Core.Tracking.Drivers.SimulatedBadge($"BADGE{i:D5}", []))
            .ToList();

        var driver = new Core.Tracking.Drivers.VisitorSimulatorDriver(settings, badges);
        var engine = new Core.Tracking.LocatingEngine(model);
        driver.Read += engine.Ingest;

        driver.StartAsync(model, CancellationToken.None).GetAwaiter().GetResult();

        // Step long enough for the agents to reach a stand and for every reader
        // port to have had a turn.
        for (int i = 0; i < 400; i++) driver.StepForTest();

        var solved = engine.SolveTick(DateTime.UtcNow);
        var truth = driver.Truth();
        var dwelling = truth.Where(t => t.DwellingAtKioskId is not null).ToList();

        output.WriteLine($"{truth.Count} agents, {dwelling.Count} standing at a stand, {solved.Count} located");

        Assert.True(engine.TotalReads > 0, "the simulator produced no reads at all");
        Assert.NotEmpty(solved);
        Assert.True(dwelling.Count > 0, "no simulated visitor ever stopped at a stand");

        // Every solved position must land in the hall its badge is really in.
        var byEpc = truth.ToDictionary(t => t.Epc);
        foreach (var tag in solved)
            Assert.Equal(byEpc[tag.Epc].HallId, tag.HallId);

        driver.StopAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void InterestedVisitorsScanQrCodesAtTheStandsTheyStopAt()
    {
        var model = TestFacility.Build();
        var category = model.Halls[0].Kiosks.First(k => k.CategoryId is not null).CategoryId!.Value;

        var settings = new SimulatorSettings
        {
            VisitorCount = 30,
            WalkSpeedMps = 4.0,
            ScanProbability = 1.0,      // everyone interested scans
            DwellScale = 0.02,          // and moves on quickly, so many stops happen
            Seed = 11,
        };

        var badges = Enumerable.Range(1, 30)
            .Select(i => new Core.Tracking.Drivers.SimulatedBadge($"BADGE{i:D5}", [category]))
            .ToList();

        var driver = new Core.Tracking.Drivers.VisitorSimulatorDriver(settings, badges);

        var scans = new List<Core.Tracking.Drivers.SimulatedScan>();
        driver.ScanRequested += scans.Add;

        driver.StartAsync(model, CancellationToken.None).GetAwaiter().GetResult();
        for (int i = 0; i < 600; i++) driver.StepForTest();

        output.WriteLine($"{scans.Count} scans raised");
        Assert.NotEmpty(scans);

        // Every scan must name a real stand.
        foreach (var scan in scans) Assert.True(model.KioskById.ContainsKey(scan.KioskId));

        driver.StopAsync().GetAwaiter().GetResult();
    }
}
