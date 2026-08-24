using Exb.Core.Configuration;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Exb.Tests;

/// <summary>
/// The mobile app's two halves: signing in from a registered email address, and
/// finding things once you are in.
///
/// These run against SQLite rather than the in-memory provider the other suites
/// use, for one specific reason: the search is built on <c>LIKE</c>, and the
/// in-memory provider cannot translate it. A test that skipped the translation
/// would prove the C# and none of the query, which is the half that breaks.
/// </summary>
public class MobileApiTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ContextFactory _factory = null!;

    private static readonly DateOnly Day = new(2026, 8, 17);

    public async Task InitializeAsync()
    {
        // One open connection keeps the in-memory database alive for the test;
        // it vanishes when the last connection closes.
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _factory = new ContextFactory(_connection);

        await using var db = _factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private sealed class ContextFactory(SqliteConnection connection) : IDbContextFactory<ExhibitionDbContext>
    {
        public ExhibitionDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<ExhibitionDbContext>()
                .UseSqlite(connection)
                .Options);
    }

    // --- signing in ----------------------------------------------------------

    [Fact]
    public async Task ARegisteredVisitorSignsInWithTheCodeEmailedToThem()
    {
        var auth = NewAuthService();

        var request = await auth.RequestCodeAsync("sara@visitor.example", "10.0.0.1");

        Assert.Equal(LoginCodeOutcome.Sent, request.Outcome);

        // Mail is on its default "outbox" provider, so the code comes back in
        // the response — otherwise nobody could sign in before SMTP is set up.
        Assert.NotNull(request.DevelopmentCode);

        var verified = await auth.VerifyAsync(
            "sara@visitor.example", request.DevelopmentCode!, "android", "Pixel 8", "1.0.0");

        Assert.Equal(VerifyOutcome.Success, verified.Outcome);
        Assert.NotNull(verified.Token);
        Assert.Equal("Sara Khan", verified.Identity!.FullName);

        // And the token works on a subsequent request.
        var resolved = await auth.ResolveAsync(verified.Token);
        Assert.Equal(verified.Identity.VisitorId, resolved!.VisitorId);
    }

    [Fact]
    public async Task TheEmailedCodeIsQueuedAndNeverStoredInTheClear()
    {
        var auth = NewAuthService();
        var request = await auth.RequestCodeAsync("sara@visitor.example", null);

        await using var db = _factory.CreateDbContext();

        var mail = await db.OutboxEmails.SingleAsync(m => m.Kind == "mobile-login");
        Assert.Contains(request.DevelopmentCode!, mail.TextBody);
        Assert.Equal("sara@visitor.example", mail.ToAddress);

        var stored = await db.VisitorLoginCodes.SingleAsync();
        Assert.DoesNotContain(request.DevelopmentCode!, stored.CodeHash);
        Assert.Equal(64, stored.CodeHash.Length);   // SHA-256, hex
    }

    [Fact]
    public async Task AnUnregisteredAddressIsAnsweredExactlyLikeARegisteredOne()
    {
        var auth = NewAuthService();

        var known = await auth.RequestCodeAsync("sara@visitor.example", null);
        var unknown = await auth.RequestCodeAsync("nobody@nowhere.example", null);

        // The outcome differs internally, but nothing a caller can see does —
        // that is what stops this endpoint being used to test who is attending.
        Assert.Equal(LoginCodeOutcome.Sent, known.Outcome);
        Assert.Equal(LoginCodeOutcome.UnknownEmail, unknown.Outcome);
        Assert.Equal(known.ExpiresInSeconds, unknown.ExpiresInSeconds);
    }

    [Fact]
    public async Task ACodeCannotBeUsedTwice()
    {
        var auth = NewAuthService();
        var request = await auth.RequestCodeAsync("sara@visitor.example", null);

        var first = await auth.VerifyAsync("sara@visitor.example", request.DevelopmentCode!, null, null, null);
        var second = await auth.VerifyAsync("sara@visitor.example", request.DevelopmentCode!, null, null, null);

        Assert.Equal(VerifyOutcome.Success, first.Outcome);
        Assert.Equal(VerifyOutcome.Expired, second.Outcome);
    }

    [Fact]
    public async Task GuessingIsGivenFiveTriesAndThenTheCodeIsDead()
    {
        var auth = NewAuthService();
        var request = await auth.RequestCodeAsync("sara@visitor.example", null);

        string wrong = request.DevelopmentCode == "000000" ? "111111" : "000000";

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            var result = await auth.VerifyAsync("sara@visitor.example", wrong, null, null, null);
            Assert.Equal(VerifyOutcome.Incorrect, result.Outcome);
        }

        Assert.Equal(
            VerifyOutcome.TooManyAttempts,
            (await auth.VerifyAsync("sara@visitor.example", wrong, null, null, null)).Outcome);

        // Even the right code is refused once the attempts are spent.
        Assert.Equal(
            VerifyOutcome.TooManyAttempts,
            (await auth.VerifyAsync("sara@visitor.example", request.DevelopmentCode!, null, null, null)).Outcome);
    }

    [Fact]
    public async Task RequestingAFreshCodeRetiresTheOldOne()
    {
        var auth = NewAuthService();

        var first = await auth.RequestCodeAsync("sara@visitor.example", null);
        var second = await auth.RequestCodeAsync("sara@visitor.example", null);

        Assert.NotEqual(first.DevelopmentCode, second.DevelopmentCode);

        // The superseded code is refused. It reads as "incorrect" rather than
        // "expired" because verification only ever considers the newest code —
        // which is also what stops two live codes doubling the guessing surface.
        Assert.Equal(
            VerifyOutcome.Incorrect,
            (await auth.VerifyAsync("sara@visitor.example", first.DevelopmentCode!, null, null, null)).Outcome);

        Assert.Equal(
            VerifyOutcome.Success,
            (await auth.VerifyAsync("sara@visitor.example", second.DevelopmentCode!, null, null, null)).Outcome);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.VisitorLoginCodes.CountAsync());
        Assert.Equal(1, await db.VisitorLoginCodes.CountAsync(c => c.ConsumedUtc != null));
    }

    [Fact]
    public async Task SigningOutOneDeviceLeavesTheOtherSignedIn()
    {
        var auth = NewAuthService();

        var phone = await SignInAsync(auth, "android");
        var tablet = await SignInAsync(auth, "ios");

        Assert.True(await auth.RevokeAsync(phone));

        Assert.Null(await auth.ResolveAsync(phone));
        Assert.NotNull(await auth.ResolveAsync(tablet));
    }

    [Fact]
    public async Task AnExpiredSessionStopsWorking()
    {
        var auth = NewAuthService();
        string token = await SignInAsync(auth, "android");

        await using (var db = _factory.CreateDbContext())
        {
            var session = await db.MobileSessions.SingleAsync();
            session.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Null(await auth.ResolveAsync(token));
    }

    [Fact]
    public async Task AVisitorCanTurnTrackingOffFromTheirOwnPhone()
    {
        var auth = NewAuthService();
        var identity = await auth.ResolveAsync(await SignInAsync(auth, "android"));

        var updated = await auth.UpdateConsentAsync(identity!.VisitorId, consentEmail: null, consentTracking: false);

        Assert.False(updated!.ConsentTracking);
        Assert.True(updated.ConsentEmail);   // untouched, because null means "leave it"
    }

    // --- finding things ------------------------------------------------------

    [Fact]
    public async Task SearchingFindsAnExhibitorByNameCategoryCountryAndStandNumber()
    {
        var directory = new MobileDirectoryService(_factory);

        Assert.Equal("Meridian Looms", (await directory.SearchExhibitorsAsync("meridian")).Items.Single().CompanyName);
        Assert.Equal("Meridian Looms", (await directory.SearchExhibitorsAsync("H1-001")).Items.Single().CompanyName);
        Assert.Equal("Bluepeak Cartons", (await directory.SearchExhibitorsAsync("Türkiye")).Items.Single().CompanyName);

        // The summary is searched too, so a visitor typing what they want rather
        // than who makes it still lands somewhere.
        Assert.Contains(
            (await directory.SearchExhibitorsAsync("shrink")).Items,
            e => e.CompanyName == "Bluepeak Cartons");
    }

    [Fact]
    public async Task FiltersNarrowByCategorySubCategoryAndHall()
    {
        var directory = new MobileDirectoryService(_factory);

        await using var db = _factory.CreateDbContext();
        int textiles = await db.Categories.Where(c => c.Code == "TEX").Select(c => c.Id).SingleAsync();
        int weaving = await db.Categories.Where(c => c.Code == "TEX-1").Select(c => c.Id).SingleAsync();
        int hall2 = await db.Halls.Where(h => h.Code == "H2").Select(h => h.Id).SingleAsync();

        Assert.Equal(2, (await directory.SearchExhibitorsAsync(categoryId: textiles)).Total);
        Assert.Equal(1, (await directory.SearchExhibitorsAsync(subCategoryId: weaving)).Total);
        Assert.Equal("Nordwind Weaving", (await directory.SearchExhibitorsAsync(hallId: hall2)).Items.Single().CompanyName);

        // Filters combine rather than replace each other.
        Assert.Empty((await directory.SearchExhibitorsAsync(categoryId: textiles, hallId: hall2, query: "meridian")).Items);
    }

    [Fact]
    public async Task ARetiredExhibitorDisappearsFromSearchButNotFromHistory()
    {
        var directory = new MobileDirectoryService(_factory);
        int retiredId;

        await using (var db = _factory.CreateDbContext())
        {
            var gone = await db.Exhibitors.SingleAsync(e => e.CompanyName == "Meridian Looms");
            gone.IsActive = false;
            retiredId = gone.Id;
            await db.SaveChangesAsync();
        }

        Assert.Empty((await directory.SearchExhibitorsAsync("meridian")).Items);
        Assert.Null(await directory.ExhibitorAsync(retiredId, visitorId: 1, day: Day));

        // The stand row survives, because visit history points at it.
        await using var check = _factory.CreateDbContext();
        Assert.True(await check.Kiosks.AnyAsync(k => k.ExhibitorId == retiredId));
    }

    [Fact]
    public async Task WildcardsTypedIntoTheSearchBoxAreTakenLiterally()
    {
        var directory = new MobileDirectoryService(_factory);

        // Without escaping, "%" would match every exhibitor at the show.
        Assert.Empty((await directory.SearchExhibitorsAsync("%")).Items);
        Assert.Empty((await directory.SearchExhibitorsAsync("_")).Items);
    }

    [Fact]
    public async Task TheCategoryTreeCarriesLiveExhibitorCounts()
    {
        var directory = new MobileDirectoryService(_factory);
        var tree = await directory.CategoryTreeAsync();

        var textiles = tree.Single(c => c.Code == "TEX");
        Assert.Equal(2, textiles.ExhibitorCount);
        Assert.Equal("Weaving", textiles.Children.Single().Name);
        Assert.Equal(1, textiles.Children.Single().ExhibitorCount);
    }

    [Fact]
    public async Task TheProgrammeIsSearchableByTitleSpeakerDayAndKind()
    {
        var directory = new MobileDirectoryService(_factory);

        Assert.Equal("Weaving in 2026", (await directory.SearchSessionsAsync("weaving")).Items.Single().Title);
        Assert.Equal("Weaving in 2026", (await directory.SearchSessionsAsync("Imran")).Items.Single().Title);

        Assert.Equal(2, (await directory.SearchSessionsAsync(date: Day)).Total);
        Assert.Equal(1, (await directory.SearchSessionsAsync(date: Day.AddDays(1))).Total);

        Assert.Equal(
            SessionKind.Workshop.ToString(),
            (await directory.SearchSessionsAsync(kind: SessionKind.Workshop)).Items.Single().Kind);
    }

    [Fact]
    public async Task AVisitorInterestedInACategoryFindsBothTheStandsAndTheTalks()
    {
        var directory = new MobileDirectoryService(_factory);

        await using var db = _factory.CreateDbContext();
        int textiles = await db.Categories.Where(c => c.Code == "TEX").Select(c => c.Id).SingleAsync();

        // This is the reason the programme shares the stands' taxonomy at all.
        Assert.Equal(2, (await directory.SearchExhibitorsAsync(categoryId: textiles)).Total);
        Assert.Equal(1, (await directory.SearchSessionsAsync(categoryId: textiles)).Total);
    }

    [Fact]
    public async Task SavingASessionToTheAgendaIsIdempotentAndReversible()
    {
        var directory = new MobileDirectoryService(_factory);

        await using var db = _factory.CreateDbContext();
        int sessionId = await db.Sessions.Where(s => s.Title == "Weaving in 2026").Select(s => s.Id).SingleAsync();

        Assert.True(await directory.BookmarkAsync(1, sessionId));
        Assert.True(await directory.BookmarkAsync(1, sessionId));   // saving twice is not two agenda entries

        var agenda = await directory.SearchSessionsAsync(visitorId: 1, bookmarkedOnly: true);
        Assert.Equal(1, agenda.Total);
        Assert.True(agenda.Items.Single().Bookmarked);

        Assert.True(await directory.RemoveBookmarkAsync(1, sessionId));
        Assert.Equal(0, (await directory.SearchSessionsAsync(visitorId: 1, bookmarkedOnly: true)).Total);
    }

    [Fact]
    public async Task OneSearchBoxAnswersAcrossExhibitorsSessionsCategoriesAndHalls()
    {
        var directory = new MobileDirectoryService(_factory);

        var result = await directory.SearchAllAsync("weaving", visitorId: 1);

        Assert.Contains(result.Exhibitors, e => e.CompanyName == "Nordwind Weaving");
        Assert.Contains(result.Sessions, s => s.Title == "Weaving in 2026");
        Assert.Contains(result.Categories, c => c.Name == "Weaving");

        var byHall = await directory.SearchAllAsync("Hall 2", visitorId: 1);
        Assert.Contains(byHall.Halls, h => h.Code == "H2");
    }

    [Fact]
    public async Task AnExhibitorPageShowsTheirStandsTheirTalksAndWhetherTheCatalogueIsAlreadyRequested()
    {
        var directory = new MobileDirectoryService(_factory);
        var catalogues = new CatalogueRequestService(_factory);

        await using var db = _factory.CreateDbContext();
        var exhibitor = await db.Exhibitors.SingleAsync(e => e.CompanyName == "Meridian Looms");
        string qr = await db.Kiosks.Where(k => k.ExhibitorId == exhibitor.Id).Select(k => k.QrToken).SingleAsync();

        var before = await directory.ExhibitorAsync(exhibitor.Id, visitorId: 1, day: Day);
        Assert.False(before!.CatalogueRequested);
        Assert.Equal("H1-001", before.Summary.Stands.Single().StandNumber);
        Assert.Equal("Weaving in 2026", before.Sessions.Single().Title);

        await catalogues.RecordAsync(qr, 1, Day, "app");

        var after = await directory.ExhibitorAsync(exhibitor.Id, visitorId: 1, day: Day);
        Assert.True(after!.CatalogueRequested);
    }

    [Fact]
    public async Task PagingReportsTheTotalRatherThanJustThePageSize()
    {
        var directory = new MobileDirectoryService(_factory);

        var page = await directory.SearchExhibitorsAsync(page: 1, pageSize: 2);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.False((await directory.SearchExhibitorsAsync(page: 2, pageSize: 2)).HasMore);
    }

    [Fact]
    public async Task PageSizeIsCappedSoOneRequestCannotAskForTheWholeDatabase()
    {
        var directory = new MobileDirectoryService(_factory);
        var page = await directory.SearchExhibitorsAsync(pageSize: 100_000);

        Assert.Equal(MobileDirectoryService.MaxPageSize, page.PageSize);
    }

    // --- what the camera actually reads --------------------------------------

    [Theory]
    // The real shape: the printed code resolves to the exhibition system.
    [InlineData("https://exhibition.example/s/QRMERIDIAN", "QRMERIDIAN")]
    [InlineData("http://10.0.0.5:5080/s/QRMERIDIAN", "QRMERIDIAN")]
    // Trailing slash, and a venue that deploys under a sub-path.
    [InlineData("https://exhibition.example/s/QRMERIDIAN/", "QRMERIDIAN")]
    [InlineData("https://venue.example/expo2026/s/QRMERIDIAN", "QRMERIDIAN")]
    // Anything appended by a printer's tracking must not become the token.
    [InlineData("https://exhibition.example/s/QRMERIDIAN?utm_source=print", "QRMERIDIAN")]
    [InlineData("https://exhibition.example/s/QRMERIDIAN#stand", "QRMERIDIAN")]
    // A bare token is what the web scan page hands over; it passes through.
    [InlineData("QRMERIDIAN", "QRMERIDIAN")]
    [InlineData("  QRMERIDIAN  ", "QRMERIDIAN")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void AScannedCodeIsReducedToItsStandToken(string? scanned, string expected)
        => Assert.Equal(expected, CatalogueRequestService.NormaliseScannedValue(scanned));

    [Fact]
    public async Task ScanningAStandsCodeAddsItToTonightsPackAndSayingSoTwiceDoesNot()
    {
        var catalogues = new CatalogueRequestService(_factory);
        const string printed = "https://exhibition.example/s/QRMERIDIAN";

        var first = await catalogues.RecordAsync(
            CatalogueRequestService.NormaliseScannedValue(printed), 1, Day, "app");
        var second = await catalogues.RecordAsync(
            CatalogueRequestService.NormaliseScannedValue(printed), 1, Day, "app");

        Assert.Equal(ScanOutcome.Added, first.Outcome);
        Assert.Equal("Meridian Looms", first.Target!.ExhibitorName);
        Assert.Equal(1, first.TodayCount);

        Assert.Equal(ScanOutcome.AlreadyRequested, second.Outcome);
        Assert.Equal(1, second.TodayCount);
    }

    [Fact]
    public async Task ACodeThatIsNotAStandAtThisShowIsRejectedRatherThanRecorded()
    {
        var catalogues = new CatalogueRequestService(_factory);

        var result = await catalogues.RecordAsync(
            CatalogueRequestService.NormaliseScannedValue("https://example.com/something-else"),
            1, Day, "app");

        Assert.Equal(ScanOutcome.UnknownStand, result.Outcome);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.CatalogueRequests.AnyAsync());
    }

    // --- fixtures ------------------------------------------------------------

    private MobileAuthService NewAuthService()
    {
        var settings = new SettingsStore(_factory);
        var mail = new MailQueue(_factory, NullLogger<MailQueue>.Instance);
        return new MobileAuthService(_factory, mail, settings, NullLogger<MobileAuthService>.Instance);
    }

    private async Task<string> SignInAsync(MobileAuthService auth, string platform)
    {
        var request = await auth.RequestCodeAsync("sara@visitor.example", null);
        var verified = await auth.VerifyAsync(
            "sara@visitor.example", request.DevelopmentCode!, platform, platform + " device", "1.0.0");

        Assert.Equal(VerifyOutcome.Success, verified.Outcome);
        return verified.Token!;
    }

    private static async Task SeedAsync(ExhibitionDbContext db)
    {
        var hall1 = new Hall { Code = "H1", Name = "Hall 1 — Machinery", WidthM = 40, DepthM = 30, DisplayOrder = 1 };
        var hall2 = new Hall { Code = "H2", Name = "Hall 2 — Automation", WidthM = 40, DepthM = 30, DisplayOrder = 2 };

        var textiles = new Category { Code = "TEX", Name = "Textile Machinery", DisplayOrder = 1 };
        var weaving = new Category { Code = "TEX-1", Name = "Weaving", Parent = textiles };
        var packaging = new Category { Code = "PKG", Name = "Packaging & Labelling", DisplayOrder = 2 };

        db.AddRange(hall1, hall2, textiles, weaving, packaging);

        var meridian = new Exhibitor
        {
            Code = "EX0001", CompanyName = "Meridian Looms", Category = textiles, SubCategory = weaving,
            Country = "Germany", Summary = "Rapier and airjet looms for industrial weaving.",
            Website = "https://meridian.example",
        };
        var bluepeak = new Exhibitor
        {
            Code = "EX0002", CompanyName = "Bluepeak Cartons", Category = packaging,
            Country = "Türkiye", Summary = "Cartoning and shrink wrap lines.",
        };
        var nordwind = new Exhibitor
        {
            Code = "EX0003", CompanyName = "Nordwind Weaving", Category = textiles,
            Country = "Italy", Summary = "Weaving preparation machinery.",
        };

        db.AddRange(
            meridian, bluepeak, nordwind,
            new Kiosk { Exhibitor = meridian, Hall = hall1, StandNumber = "H1-001", X = 3, Y = 3, QrToken = "QRMERIDIAN" },
            new Kiosk { Exhibitor = bluepeak, Hall = hall1, StandNumber = "H1-002", X = 9, Y = 3, QrToken = "QRBLUEPEAK" },
            new Kiosk { Exhibitor = nordwind, Hall = hall2, StandNumber = "H2-001", X = 3, Y = 3, QrToken = "QRNORDWIND" });

        db.Visitors.Add(new Visitor
        {
            Id = 1,
            FullName = "Sara Khan",
            Email = "sara@visitor.example",
            BadgeEpc = "3034257BF4A1B2C3D4E5F607",
            RegistrationCode = "AAAA-BBBB",
            AccessToken = "TOKENSARA",
            Company = "Khan Textiles",
            ConsentEmail = true,
            ConsentTracking = true,
        });

        db.Sessions.AddRange(
            new ProgrammeSession
            {
                Code = "S0001", Title = "Weaving in 2026", Kind = SessionKind.Lecture,
                SpeakerName = "Imran Malik", SpeakerOrganisation = "Meridian Looms",
                Exhibitor = meridian, Category = textiles, SubCategory = weaving,
                Hall = hall1, RoomName = "Main Theatre",
                EventDate = Day, StartsAt = new TimeOnly(11, 0), EndsAt = new TimeOnly(11, 45),
                Capacity = 120, Language = "en",
            },
            new ProgrammeSession
            {
                Code = "S0002", Title = "Commissioning cartoning lines", Kind = SessionKind.Workshop,
                SpeakerName = "Elena Rossi", Category = packaging,
                RoomName = "Seminar Room A",
                EventDate = Day, StartsAt = new TimeOnly(14, 0), EndsAt = new TimeOnly(15, 30),
                Capacity = 30, RequiresBooking = true, Language = "en",
            },
            new ProgrammeSession
            {
                Code = "S0003", Title = "Opening ceremony", Kind = SessionKind.Ceremony,
                RoomName = "Main Theatre",
                EventDate = Day.AddDays(1), StartsAt = new TimeOnly(10, 30), EndsAt = new TimeOnly(11, 0),
            });

        await db.SaveChangesAsync();
    }
}
