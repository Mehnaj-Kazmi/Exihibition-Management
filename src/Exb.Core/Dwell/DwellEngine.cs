using System.Collections.Concurrent;
using Exb.Core.Configuration;
using Exb.Core.Facility;
using Exb.Core.Tracking;

namespace Exb.Core.Dwell;

/// <summary>Interest strength of a stand visit. Mirrors Exb.Data InterestLevel.</summary>
public enum DwellLevel
{
    PassBy = 0,
    Browsed = 1,
    Interested = 2,
    Strong = 3,
}

/// <summary>Who a badge belongs to, and whether they agreed to be tracked.</summary>
public sealed record BadgeHolder(int VisitorId, bool ConsentTracking);

/// <summary>Resolves a badge EPC to a registered visitor. Implemented over the database, cached.</summary>
public interface IBadgeDirectory
{
    BadgeHolder? Resolve(string epc);
}

/// <summary>One continuous stop at one stand, while it is still being measured.</summary>
public sealed class VisitSession
{
    /// <summary>Database id once the open session has been written. Zero until then.</summary>
    public long Id { get; set; }

    public required int VisitorId { get; init; }
    public required int KioskId { get; init; }
    public required int ExhibitorId { get; init; }
    public required int HallId { get; init; }
    public int? CategoryId { get; init; }
    public int? SubCategoryId { get; init; }

    public required DateOnly EventDate { get; init; }
    public required DateTime StartedUtc { get; init; }
    public DateTime LastSeenUtc { get; set; }

    public int SampleCount { get; set; }
    public double ConfidenceSum { get; set; }
    public double MarginSum { get; set; }

    public bool IsClosed { get; set; }

    /// <summary>Set when the in-memory state has moved on from what the database holds.</summary>
    public bool IsDirty { get; set; }

    public int DwellSeconds => Math.Max(0, (int)Math.Round((LastSeenUtc - StartedUtc).TotalSeconds));
    public double MeanConfidence => SampleCount == 0 ? 0 : Math.Round(ConfidenceSum / SampleCount, 3);
    public double MeanMarginM => SampleCount == 0 ? 0 : Math.Round(MarginSum / SampleCount, 3);

    /// <summary>
    /// How much interest this stop represents.
    ///
    /// Dwell time sets the level, and then ambiguous attribution costs one level.
    /// A ten-minute stop measured on a tight row of shell schemes, where the
    /// neighbouring stand was almost as close on average, is reported as
    /// interest rather than as a strong lead — because we genuinely do not know
    /// which of the two stands held them. Downgrading is the honest move;
    /// discarding would throw away a real visit, and keeping it would sell an
    /// exhibitor a lead that might be their neighbour's.
    /// </summary>
    public DwellLevel LevelFor(DwellSettings s)
    {
        int dwell = DwellSeconds;
        DwellLevel level =
            dwell >= s.StrongSeconds ? DwellLevel.Strong :
            dwell >= s.InterestSeconds ? DwellLevel.Interested :
            dwell >= s.MinDwellSeconds ? DwellLevel.Browsed :
            DwellLevel.PassBy;

        if (level > DwellLevel.PassBy && MeanMarginM < s.MinMarginM)
            level--;

        return level;
    }
}

public enum SessionChangeKind { Opened, Updated, Closed }

public sealed record SessionChange(SessionChangeKind Kind, VisitSession Session);

/// <summary>
/// Turns a stream of solved badge positions into stand visits.
///
/// A session opens when a badge is attributed to a stand with a usable fix, and
/// closes when the badge moves to another stand, goes quiet for longer than the
/// break window, or hits the session cap. Only closed sessions past the minimum
/// dwell become interest; everything shorter is kept as a pass-by so that
/// footfall past a stand can still be reported without inflating leads.
///
/// State is held in memory and flushed by the caller, because writing a row per
/// position solve would mean tens of thousands of writes a minute for no gain.
/// </summary>
public sealed class DwellEngine(IBadgeDirectory badges)
{
    private readonly ConcurrentDictionary<int, VisitSession> _openByVisitor = new();

    public IReadOnlyCollection<VisitSession> OpenSessions => _openByVisitor.Values.ToList();

    public VisitSession? OpenSessionFor(int visitorId) => _openByVisitor.GetValueOrDefault(visitorId);

    /// <summary>Restore in-flight sessions after a restart, so a visit is not split in two.</summary>
    public void Restore(IEnumerable<VisitSession> sessions)
    {
        foreach (var s in sessions)
            if (!s.IsClosed) _openByVisitor[s.VisitorId] = s;
    }

    /// <summary>
    /// Attribute this tick's updated positions and advance the open sessions.
    /// Returns every session that opened, materially changed, or closed.
    /// </summary>
    public IReadOnlyList<SessionChange> Tick(
        FacilityModel facility,
        DwellSettings settings,
        IReadOnlyList<TrackedTag> updated,
        DateTime nowUtc,
        DateOnly eventDate)
    {
        var changes = new List<SessionChange>();

        foreach (var tag in updated)
        {
            var holder = badges.Resolve(tag.Epc);
            if (holder is null || !holder.ConsentTracking)
            {
                tag.AttributedKioskId = null;
                continue;
            }

            var hall = facility.HallById.GetValueOrDefault(tag.HallId);
            if (hall is null) continue;

            // A fix we do not believe should neither open a visit nor extend one.
            // Leaving the existing session alone means a momentary bad solve does
            // not chop a real ten-minute stop into two five-minute ones.
            if (tag.Confidence < settings.MinConfidence) continue;

            var attribution = KioskAttributor.Attribute(hall, tag.X, tag.Y, settings.AttachRadiusM);

            tag.AttributedKioskId = attribution?.KioskId;
            tag.AttributionMarginM = attribution?.MarginM ?? 0;

            if (attribution is null) continue;

            var kiosk = facility.KioskById.GetValueOrDefault(attribution.Value.KioskId);
            if (kiosk is null) continue;

            var current = _openByVisitor.GetValueOrDefault(holder.VisitorId);

            if (current is not null && current.KioskId != kiosk.Id)
            {
                Close(current, changes, current.LastSeenUtc);
                current = null;
            }

            if (current is null)
            {
                var session = new VisitSession
                {
                    VisitorId = holder.VisitorId,
                    KioskId = kiosk.Id,
                    ExhibitorId = kiosk.ExhibitorId,
                    HallId = kiosk.HallId,
                    CategoryId = kiosk.CategoryId,
                    SubCategoryId = kiosk.SubCategoryId,
                    EventDate = eventDate,
                    StartedUtc = tag.LastSeenUtc,
                    LastSeenUtc = tag.LastSeenUtc,
                    SampleCount = 1,
                    ConfidenceSum = tag.Confidence,
                    MarginSum = attribution.Value.MarginM,
                    IsDirty = true,
                };
                _openByVisitor[holder.VisitorId] = session;
                changes.Add(new SessionChange(SessionChangeKind.Opened, session));
                continue;
            }

            current.LastSeenUtc = tag.LastSeenUtc > current.LastSeenUtc ? tag.LastSeenUtc : current.LastSeenUtc;
            current.SampleCount++;
            current.ConfidenceSum += tag.Confidence;
            current.MarginSum += attribution.Value.MarginM;
            current.IsDirty = true;

            if (current.DwellSeconds >= settings.MaxSessionSeconds)
                Close(current, changes, current.StartedUtc.AddSeconds(settings.MaxSessionSeconds));
            else
                changes.Add(new SessionChange(SessionChangeKind.Updated, current));
        }

        // Close sessions whose badge has gone quiet, whether or not it appeared
        // in this tick's updates. A visitor who walks out of the hall never
        // produces another position, so nothing else would ever close them.
        foreach (var session in _openByVisitor.Values)
        {
            if (session.IsClosed) continue;
            if ((nowUtc - session.LastSeenUtc).TotalSeconds >= settings.BreakSeconds)
                Close(session, changes, session.LastSeenUtc);
        }

        return changes;
    }

    /// <summary>Close everything, e.g. at end of day or on shutdown.</summary>
    public IReadOnlyList<SessionChange> CloseAll(DateTime nowUtc)
    {
        var changes = new List<SessionChange>();
        foreach (var session in _openByVisitor.Values)
            if (!session.IsClosed)
                Close(session, changes, session.LastSeenUtc);
        return changes;
    }

    private void Close(VisitSession session, List<SessionChange> changes, DateTime endedUtc)
    {
        session.LastSeenUtc = endedUtc;
        session.IsClosed = true;
        session.IsDirty = true;
        _openByVisitor.TryRemove(session.VisitorId, out _);
        changes.Add(new SessionChange(SessionChangeKind.Closed, session));
    }
}
