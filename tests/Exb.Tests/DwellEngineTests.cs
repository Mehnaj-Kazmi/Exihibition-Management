using Exb.Core.Configuration;
using Exb.Core.Dwell;
using Exb.Core.Facility;
using Exb.Core.Tracking;
using Xunit;

namespace Exb.Tests;

/// <summary>
/// Dwell sessions are driven from explicit timestamps rather than from the wall
/// clock, so a ten-minute stand visit can be tested in a millisecond and the
/// thresholds are exercised exactly rather than approximately.
/// </summary>
public class DwellEngineTests
{
    private static readonly DateTime Start = new(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 8, 17);

    private sealed class StubDirectory(int visitorId = 1, bool consent = true) : IBadgeDirectory
    {
        public BadgeHolder? Resolve(string epc) => epc == "UNKNOWN" ? null : new BadgeHolder(visitorId, consent);
    }

    private static (FacilityModel Model, KioskSpec Kiosk, KioskSpec Neighbour) Floor()
    {
        var model = TestFacility.Build();
        var hall = model.Halls[0];
        return (model, hall.Kiosks[0], hall.Kiosks[1]);
    }

    private static TrackedTag TagAt(KioskSpec kiosk, DateTime seen, double confidence = 0.8)
        => new()
        {
            Epc = "BADGE001",
            HallId = kiosk.HallId,
            HallCode = "H1",
            X = kiosk.Footprint.CentreX,
            Y = kiosk.Footprint.CentreY,
            Confidence = confidence,
            LastSeenUtc = seen,
            FirstSeenUtc = seen,
            Status = TagStatus.Live,
        };

    [Fact]
    public void ASustainedStopBecomesAVisitWithTheRightInterestLevel()
    {
        var (model, kiosk, _) = Floor();
        var settings = new DwellSettings();
        var engine = new DwellEngine(new StubDirectory());

        // Arrive, stay four minutes, then leave.
        engine.Tick(model, settings, [TagAt(kiosk, Start)], Start, Day);
        var atEnd = Start.AddSeconds(240);
        engine.Tick(model, settings, [TagAt(kiosk, atEnd)], atEnd, Day);

        var afterLeaving = atEnd.AddSeconds(settings.BreakSeconds + 5);
        var changes = engine.Tick(model, settings, [], afterLeaving, Day);

        var closed = changes.Single(c => c.Kind == SessionChangeKind.Closed).Session;

        Assert.Equal(kiosk.Id, closed.KioskId);
        Assert.Equal(240, closed.DwellSeconds);
        Assert.Equal(DwellLevel.Strong, closed.LevelFor(settings));
        Assert.Empty(engine.OpenSessions);
    }

    [Theory]
    [InlineData(10, DwellLevel.PassBy)]
    [InlineData(30, DwellLevel.Browsed)]
    [InlineData(60, DwellLevel.Interested)]
    [InlineData(400, DwellLevel.Strong)]
    public void DwellTimeSetsTheInterestLevel(int seconds, DwellLevel expected)
    {
        var (model, kiosk, _) = Floor();
        var settings = new DwellSettings();
        var engine = new DwellEngine(new StubDirectory());

        engine.Tick(model, settings, [TagAt(kiosk, Start)], Start, Day);
        var end = Start.AddSeconds(seconds);
        engine.Tick(model, settings, [TagAt(kiosk, end)], end, Day);

        var session = engine.OpenSessionFor(1)!;
        Assert.Equal(seconds, session.DwellSeconds);
        Assert.Equal(expected, session.LevelFor(settings));
    }

    [Fact]
    public void AnAmbiguousAttributionIsReportedOneLevelLowerRatherThanGuessed()
    {
        var (model, kiosk, _) = Floor();
        var settings = new DwellSettings();

        var confident = new VisitSession
        {
            VisitorId = 1, KioskId = kiosk.Id, ExhibitorId = 1, HallId = kiosk.HallId,
            EventDate = Day, StartedUtc = Start, LastSeenUtc = Start.AddSeconds(300),
            SampleCount = 10, ConfidenceSum = 8, MarginSum = 10,   // mean margin 1.0 m
        };

        var ambiguous = new VisitSession
        {
            VisitorId = 2, KioskId = kiosk.Id, ExhibitorId = 1, HallId = kiosk.HallId,
            EventDate = Day, StartedUtc = Start, LastSeenUtc = Start.AddSeconds(300),
            SampleCount = 10, ConfidenceSum = 8, MarginSum = 0.5,  // mean margin 0.05 m
        };

        Assert.Equal(DwellLevel.Strong, confident.LevelFor(settings));
        Assert.Equal(DwellLevel.Interested, ambiguous.LevelFor(settings));
    }

    [Fact]
    public void MovingToTheNextStandClosesTheFirstVisitAndOpensAnother()
    {
        var (model, first, second) = Floor();
        var settings = new DwellSettings();
        var engine = new DwellEngine(new StubDirectory());

        engine.Tick(model, settings, [TagAt(first, Start)], Start, Day);

        var later = Start.AddSeconds(90);
        var changes = engine.Tick(model, settings, [TagAt(second, later)], later, Day);

        var closed = changes.Single(c => c.Kind == SessionChangeKind.Closed).Session;
        var opened = changes.Single(c => c.Kind == SessionChangeKind.Opened).Session;

        Assert.Equal(first.Id, closed.KioskId);
        Assert.Equal(second.Id, opened.KioskId);
        Assert.Single(engine.OpenSessions);
    }

    [Fact]
    public void AMomentaryBadFixDoesNotChopOneLongVisitIntoTwo()
    {
        var (model, kiosk, _) = Floor();
        var settings = new DwellSettings();
        var engine = new DwellEngine(new StubDirectory());

        engine.Tick(model, settings, [TagAt(kiosk, Start)], Start, Day);

        // A single low-confidence solve arrives mid-visit and must be ignored.
        var wobble = Start.AddSeconds(20);
        engine.Tick(model, settings, [TagAt(kiosk, wobble, confidence: 0.05)], wobble, Day);

        var end = Start.AddSeconds(200);
        engine.Tick(model, settings, [TagAt(kiosk, end)], end, Day);

        var session = engine.OpenSessionFor(1)!;
        Assert.Equal(200, session.DwellSeconds);
        Assert.Single(engine.OpenSessions);
    }

    [Fact]
    public void ABadgeLeftOnACounterIsCappedRatherThanBecomingAnEightHourConversation()
    {
        var (model, kiosk, _) = Floor();
        var settings = new DwellSettings { MaxSessionSeconds = 600 };
        var engine = new DwellEngine(new StubDirectory());

        engine.Tick(model, settings, [TagAt(kiosk, Start)], Start, Day);

        var muchLater = Start.AddHours(8);
        var changes = engine.Tick(model, settings, [TagAt(kiosk, muchLater)], muchLater, Day);

        var closed = changes.Single(c => c.Kind == SessionChangeKind.Closed).Session;
        Assert.Equal(600, closed.DwellSeconds);
    }

    [Fact]
    public void AVisitorWhoDeclinedTrackingIsNotMeasured()
    {
        var (model, kiosk, _) = Floor();
        var engine = new DwellEngine(new StubDirectory(consent: false));

        var tag = TagAt(kiosk, Start);
        var changes = engine.Tick(model, new DwellSettings(), [tag], Start, Day);

        Assert.Empty(changes);
        Assert.Empty(engine.OpenSessions);
        Assert.Null(tag.AttributedKioskId);
    }

    [Fact]
    public void AnUnregisteredBadgeIsLocatedButNotAttributedToAnyone()
    {
        var (model, kiosk, _) = Floor();
        var engine = new DwellEngine(new StubDirectory());

        var tag = new TrackedTag
        {
            Epc = "UNKNOWN",
            HallId = kiosk.HallId,
            HallCode = "H1",
            X = kiosk.Footprint.CentreX,
            Y = kiosk.Footprint.CentreY,
            Confidence = 0.8,
            LastSeenUtc = Start,
            Status = TagStatus.Live,
        };

        Assert.Empty(engine.Tick(model, new DwellSettings(), [tag], Start, Day));
    }

    [Fact]
    public void WalkingDownAnAisleFarFromAnyStandOpensNothing()
    {
        var model = TestFacility.Build();
        var hall = model.Halls[0];
        var engine = new DwellEngine(new StubDirectory());

        // A point in the perimeter gangway, beyond the attach radius of any stand.
        var tag = new TrackedTag
        {
            Epc = "BADGE001",
            HallId = hall.Id,
            HallCode = hall.Code,
            X = 0.2,
            Y = 0.2,
            Confidence = 0.9,
            LastSeenUtc = Start,
            Status = TagStatus.Live,
        };

        Assert.Empty(engine.Tick(model, new DwellSettings(), [tag], Start, Day));
        Assert.Null(tag.AttributedKioskId);
    }

    [Fact]
    public void RestoredSessionsContinueRatherThanStartingAgain()
    {
        var (model, kiosk, _) = Floor();
        var settings = new DwellSettings();
        var engine = new DwellEngine(new StubDirectory());

        engine.Restore([new VisitSession
        {
            Id = 77,
            VisitorId = 1, KioskId = kiosk.Id, ExhibitorId = 1, HallId = kiosk.HallId,
            EventDate = Day, StartedUtc = Start, LastSeenUtc = Start.AddSeconds(30),
            SampleCount = 5, ConfidenceSum = 4, MarginSum = 5,
        }]);

        var later = Start.AddSeconds(120);
        var changes = engine.Tick(model, settings, [TagAt(kiosk, later)], later, Day);

        Assert.All(changes, c => Assert.Equal(SessionChangeKind.Updated, c.Kind));
        var session = engine.OpenSessionFor(1)!;
        Assert.Equal(77, session.Id);
        Assert.Equal(120, session.DwellSeconds);
    }

    [Fact]
    public void ClosingTheHallsClosesEverythingLeftOpen()
    {
        var (model, kiosk, _) = Floor();
        var engine = new DwellEngine(new StubDirectory());

        engine.Tick(model, new DwellSettings(), [TagAt(kiosk, Start)], Start, Day);
        Assert.Single(engine.OpenSessions);

        var changes = engine.CloseAll(Start.AddHours(1));

        Assert.Single(changes);
        Assert.Equal(SessionChangeKind.Closed, changes[0].Kind);
        Assert.Empty(engine.OpenSessions);
    }
}
