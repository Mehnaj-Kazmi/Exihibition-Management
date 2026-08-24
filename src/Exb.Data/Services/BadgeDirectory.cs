using System.Collections.Concurrent;
using Exb.Core.Dwell;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data.Services;

/// <summary>
/// Resolves a badge EPC to the visitor carrying it.
///
/// This sits on the hot path: it is consulted for every badge on every solve
/// tick, several hundred times a second on a busy floor, so it is an in-memory
/// dictionary refreshed in the background rather than a query. Registration
/// pushes new badges in directly, so a visitor is trackable the moment their
/// badge is issued instead of waiting for the next refresh.
/// </summary>
public sealed class BadgeDirectory(IDbContextFactory<ExhibitionDbContext> factory) : IBadgeDirectory
{
    private ConcurrentDictionary<string, BadgeHolder> _byEpc = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byEpc.Count;
    public DateTime LastRefreshedUtc { get; private set; }

    public BadgeHolder? Resolve(string epc) => _byEpc.GetValueOrDefault(epc);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.Visitors
            .AsNoTracking()
            .Where(v => v.IsActive && v.BadgeEpc != "")
            .Select(v => new { v.BadgeEpc, v.Id, v.ConsentTracking })
            .ToListAsync(ct);

        var next = new ConcurrentDictionary<string, BadgeHolder>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            next[row.BadgeEpc] = new BadgeHolder(row.Id, row.ConsentTracking);

        _byEpc = next;
        LastRefreshedUtc = DateTime.UtcNow;
    }

    /// <summary>Register or update one badge immediately, without waiting for a refresh.</summary>
    public void Upsert(string epc, int visitorId, bool consentTracking)
    {
        if (string.IsNullOrWhiteSpace(epc)) return;
        _byEpc[epc] = new BadgeHolder(visitorId, consentTracking);
    }

    public void Remove(string epc)
    {
        if (!string.IsNullOrWhiteSpace(epc)) _byEpc.TryRemove(epc, out _);
    }
}
