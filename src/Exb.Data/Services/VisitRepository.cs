using Exb.Core.Configuration;
using Exb.Core.Dwell;
using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data.Services;

/// <summary>
/// Persists stand visits as the dwell engine produces them.
///
/// Open sessions are written straight away, so a stand's own screen can show who
/// is standing there now, and then updated on a throttle rather than on every
/// position solve — the difference between a few writes a minute and tens of
/// thousands. Closing a session is what fixes the interest level, since that is
/// the first moment the total dwell time is actually known.
/// </summary>
public sealed class VisitRepository(IDbContextFactory<ExhibitionDbContext> factory)
{
    public async Task ApplyAsync(
        IReadOnlyList<SessionChange> changes,
        DwellSettings settings,
        CancellationToken ct = default)
    {
        if (changes.Count == 0) return;

        await using var db = await factory.CreateDbContextAsync(ct);

        // One session can appear several times in a batch (opened then closed in
        // the same tick, for instance). Keep only the final state of each.
        var latest = new Dictionary<VisitSession, SessionChangeKind>();
        foreach (var change in changes) latest[change.Session] = change.Kind;

        foreach (var (session, kind) in latest)
        {
            if (kind == SessionChangeKind.Opened || session.Id == 0)
            {
                var row = new VisitorVisit
                {
                    VisitorId = session.VisitorId,
                    KioskId = session.KioskId,
                    ExhibitorId = session.ExhibitorId,
                    HallId = session.HallId,
                    CategoryId = session.CategoryId,
                    SubCategoryId = session.SubCategoryId,
                    EventDate = session.EventDate,
                    StartedUtc = session.StartedUtc,
                    EndedUtc = session.LastSeenUtc,
                    DwellSeconds = session.DwellSeconds,
                    SampleCount = session.SampleCount,
                    MeanConfidence = session.MeanConfidence,
                    MeanMarginM = session.MeanMarginM,
                    Level = (InterestLevel)session.LevelFor(settings),
                    IsOpen = !session.IsClosed,
                };
                db.Visits.Add(row);
                await db.SaveChangesAsync(ct);
                session.Id = row.Id;
            }
            else
            {
                var row = await db.Visits.FirstOrDefaultAsync(v => v.Id == session.Id, ct);
                if (row is null) continue;

                row.EndedUtc = session.LastSeenUtc;
                row.DwellSeconds = session.DwellSeconds;
                row.SampleCount = session.SampleCount;
                row.MeanConfidence = session.MeanConfidence;
                row.MeanMarginM = session.MeanMarginM;
                row.Level = (InterestLevel)session.LevelFor(settings);
                row.IsOpen = !session.IsClosed;
            }

            session.IsDirty = false;
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reload sessions that were still open when the process stopped, so a
    /// restart mid-afternoon does not split someone's long stand visit in two
    /// or leave a phantom row open forever.
    /// </summary>
    public async Task<IReadOnlyList<VisitSession>> LoadOpenAsync(DwellSettings settings, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.Visits.Where(v => v.IsOpen).ToListAsync(ct);
        var restored = new List<VisitSession>();
        var cutoff = DateTime.UtcNow.AddSeconds(-settings.BreakSeconds);

        foreach (var row in rows)
        {
            if (row.EndedUtc < cutoff)
            {
                // Too old to still be standing there. Close it where it stopped.
                row.IsOpen = false;
                row.Level = (InterestLevel)LevelFor(row, settings);
                continue;
            }

            restored.Add(new VisitSession
            {
                Id = row.Id,
                VisitorId = row.VisitorId,
                KioskId = row.KioskId,
                ExhibitorId = row.ExhibitorId,
                HallId = row.HallId,
                CategoryId = row.CategoryId,
                SubCategoryId = row.SubCategoryId,
                EventDate = row.EventDate,
                StartedUtc = row.StartedUtc,
                LastSeenUtc = row.EndedUtc,
                SampleCount = row.SampleCount,
                ConfidenceSum = row.MeanConfidence * row.SampleCount,
                MarginSum = row.MeanMarginM * row.SampleCount,
            });
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return restored;
    }

    /// <summary>Close anything left open, e.g. when the halls shut for the night.</summary>
    public async Task<int> CloseAllOpenAsync(DwellSettings settings, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Visits.Where(v => v.IsOpen).ToListAsync(ct);

        foreach (var row in rows)
        {
            row.IsOpen = false;
            row.Level = (InterestLevel)LevelFor(row, settings);
        }

        if (rows.Count > 0) await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>Same classification the dwell engine applies, for rows closed outside it.</summary>
    private static DwellLevel LevelFor(VisitorVisit row, DwellSettings s)
    {
        DwellLevel level =
            row.DwellSeconds >= s.StrongSeconds ? DwellLevel.Strong :
            row.DwellSeconds >= s.InterestSeconds ? DwellLevel.Interested :
            row.DwellSeconds >= s.MinDwellSeconds ? DwellLevel.Browsed :
            DwellLevel.PassBy;

        if (level > DwellLevel.PassBy && row.MeanMarginM < s.MinMarginM) level--;
        return level;
    }
}
