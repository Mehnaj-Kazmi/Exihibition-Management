using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exb.Data.Services;

// --- what the app is handed --------------------------------------------------

public sealed record StandSummary(int KioskId, string StandNumber, int HallId, string HallCode, string HallName);

public sealed record ExhibitorSummary(
    int Id,
    string Code,
    string CompanyName,
    string? Country,
    string? Summary,
    int? CategoryId,
    string? CategoryName,
    int? SubCategoryId,
    string? SubCategoryName,
    IReadOnlyList<StandSummary> Stands,
    int CatalogueCount);

public sealed record ExhibitorDetail(
    ExhibitorSummary Summary,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Website,
    string? LogoPath,
    IReadOnlyList<SessionSummary> Sessions,
    bool CatalogueRequested);

public sealed record CategoryNode(
    int Id, string Code, string Name, string? Colour, string? Description,
    int ExhibitorCount, IReadOnlyList<CategoryNode> Children);

public sealed record HallSummary(
    int Id, string Code, string Name, double WidthM, double DepthM,
    int StandCount, int ExhibitorCount, string? Notes);

public sealed record HallDetail(HallSummary Summary, IReadOnlyList<ExhibitorSummary> Exhibitors, int SessionCount);

public sealed record SessionSummary(
    int Id,
    string Code,
    string Title,
    string Kind,
    string? SpeakerName,
    string? SpeakerTitle,
    string? SpeakerOrganisation,
    DateOnly EventDate,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int? HallId,
    string? HallName,
    string? RoomName,
    int? CategoryId,
    string? CategoryName,
    int? SubCategoryId,
    string? SubCategoryName,
    int? ExhibitorId,
    string? ExhibitorName,
    bool RequiresBooking,
    int Capacity,
    string? Language,
    bool Bookmarked);

public sealed record SessionDetail(SessionSummary Summary, string? Abstract);

public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize)
{
    public bool HasMore => PageNumber * PageSize < Total;
}

public sealed record UnifiedSearchResult(
    IReadOnlyList<ExhibitorSummary> Exhibitors,
    IReadOnlyList<SessionSummary> Sessions,
    IReadOnlyList<CategoryNode> Categories,
    IReadOnlyList<HallSummary> Halls,
    int ExhibitorTotal,
    int SessionTotal);

/// <summary>
/// Everything the mobile app reads.
///
/// It is a separate service from <see cref="InterestQueryService"/> on purpose:
/// that one exists to feed the interest analyser and the evening report, and its
/// shapes follow what those need. This one answers a visitor standing in an
/// aisle with a phone, so it pages, it searches on the fields people actually
/// type, and it never returns anything the visitor is not entitled to see.
///
/// Every query filters on IsActive at both ends — a retired exhibitor's stand
/// must not surface just because the stand row is still there for last year's
/// visit history.
/// </summary>
public sealed class MobileDirectoryService(IDbContextFactory<ExhibitionDbContext> factory)
{
    public const int MaxPageSize = 100;

    // --- exhibitors ----------------------------------------------------------

    public async Task<Page<ExhibitorSummary>> SearchExhibitorsAsync(
        string? query = null,
        int? categoryId = null,
        int? subCategoryId = null,
        int? hallId = null,
        string? country = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        (page, pageSize) = Normalise(page, pageSize);

        await using var db = await factory.CreateDbContextAsync(ct);

        var q = db.Exhibitors.AsNoTracking().Where(e => e.IsActive);

        if (categoryId is { } cat) q = q.Where(e => e.CategoryId == cat);
        if (subCategoryId is { } sub) q = q.Where(e => e.SubCategoryId == sub);
        if (hallId is { } hall) q = q.Where(e => e.Kiosks.Any(k => k.IsActive && k.HallId == hall));
        if (!string.IsNullOrWhiteSpace(country)) q = q.Where(e => e.Country == country);

        string? term = Clean(query);
        if (term is not null)
        {
            // Stand number is in here because half of what a visitor types into
            // a search box at a show is "B-142" off a floor plan, not a company.
            q = q.Where(e =>
                EF.Functions.Like(e.CompanyName, $"%{term}%")
                || EF.Functions.Like(e.Code, $"%{term}%")
                || (e.Summary != null && EF.Functions.Like(e.Summary, $"%{term}%"))
                || (e.Country != null && EF.Functions.Like(e.Country, $"%{term}%"))
                || e.Kiosks.Any(k => k.IsActive && EF.Functions.Like(k.StandNumber, $"%{term}%")));
        }

        int total = await q.CountAsync(ct);

        var rows = await q
            .OrderBy(e => e.CompanyName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id, e.Code, e.CompanyName, e.Country, e.Summary,
                e.CategoryId,
                CategoryName = e.Category != null ? e.Category.Name : null,
                e.SubCategoryId,
                SubCategoryName = e.SubCategory != null ? e.SubCategory.Name : null,
                Stands = e.Kiosks
                    .Where(k => k.IsActive)
                    .OrderBy(k => k.StandNumber)
                    .Select(k => new StandSummary(k.Id, k.StandNumber, k.HallId, k.Hall.Code, k.Hall.Name))
                    .ToList(),
                CatalogueCount = e.Catalogues.Count(c => c.IsActive),
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new ExhibitorSummary(
                r.Id, r.Code, r.CompanyName, r.Country, r.Summary,
                r.CategoryId, r.CategoryName, r.SubCategoryId, r.SubCategoryName,
                r.Stands, r.CatalogueCount))
            .ToList();

        return new Page<ExhibitorSummary>(items, total, page, pageSize);
    }

    public async Task<ExhibitorDetail?> ExhibitorAsync(
        int exhibitorId, int? visitorId, DateOnly day, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var e = await db.Exhibitors
            .AsNoTracking()
            .Where(x => x.Id == exhibitorId && x.IsActive)
            .Select(x => new
            {
                x.Id, x.Code, x.CompanyName, x.Country, x.Summary,
                x.CategoryId,
                CategoryName = x.Category != null ? x.Category.Name : null,
                x.SubCategoryId,
                SubCategoryName = x.SubCategory != null ? x.SubCategory.Name : null,
                x.ContactName, x.Email, x.Phone, x.Website, x.LogoPath,
                Stands = x.Kiosks
                    .Where(k => k.IsActive)
                    .OrderBy(k => k.StandNumber)
                    .Select(k => new StandSummary(k.Id, k.StandNumber, k.HallId, k.Hall.Code, k.Hall.Name))
                    .ToList(),
                CatalogueCount = x.Catalogues.Count(c => c.IsActive),
            })
            .FirstOrDefaultAsync(ct);

        if (e is null) return null;

        var summary = new ExhibitorSummary(
            e.Id, e.Code, e.CompanyName, e.Country, e.Summary,
            e.CategoryId, e.CategoryName, e.SubCategoryId, e.SubCategoryName,
            e.Stands, e.CatalogueCount);

        var hosted = db.Sessions
            .AsNoTracking()
            .Where(s => s.IsActive && s.ExhibitorId == exhibitorId)
            .OrderBy(s => s.EventDate).ThenBy(s => s.StartsAt);

        var sessions = await Project(hosted, visitorId).ToListAsync(ct);

        bool requested = visitorId is { } vid && await db.CatalogueRequests
            .AnyAsync(r => r.VisitorId == vid && r.ExhibitorId == exhibitorId && r.EventDate == day && r.Included, ct);

        return new ExhibitorDetail(
            summary, e.ContactName, e.Email, e.Phone, e.Website, e.LogoPath, sessions, requested);
    }

    /// <summary>The countries represented, for the filter list. Empty entries are dropped.</summary>
    public async Task<IReadOnlyList<string>> CountriesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Exhibitors
            .AsNoTracking()
            .Where(e => e.IsActive && e.Country != null && e.Country != "")
            .Select(e => e.Country!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
    }

    // --- categories ----------------------------------------------------------

    /// <summary>
    /// The whole taxonomy as a tree, with a live exhibitor count on every node.
    /// The app fetches this once at start-up and drives both the browse screen
    /// and the filter pickers from it, so this is one round trip rather than one
    /// per category the visitor opens.
    /// </summary>
    public async Task<IReadOnlyList<CategoryNode>> CategoryTreeAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Code, c.Name, c.Colour, c.Description, c.ParentId })
            .ToListAsync(ct);

        // Counted in one pass at each level rather than per node: a correlated
        // count inside the projection would be one query per sub-category.
        var byCategory = await db.Exhibitors
            .AsNoTracking()
            .Where(e => e.IsActive && e.CategoryId != null)
            .GroupBy(e => e.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var bySubCategory = await db.Exhibitors
            .AsNoTracking()
            .Where(e => e.IsActive && e.SubCategoryId != null)
            .GroupBy(e => e.SubCategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var children = categories.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);

        return categories
            .Where(c => c.ParentId is null)
            .Select(c => new CategoryNode(
                c.Id, c.Code, c.Name, c.Colour, c.Description,
                byCategory.GetValueOrDefault(c.Id),
                children[c.Id]
                    .Select(s => new CategoryNode(
                        s.Id, s.Code, s.Name, s.Colour, s.Description,
                        bySubCategory.GetValueOrDefault(s.Id),
                        []))
                    .ToList()))
            .ToList();
    }

    // --- halls ---------------------------------------------------------------

    public async Task<IReadOnlyList<HallSummary>> HallsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Halls
            .AsNoTracking()
            .Where(h => h.IsActive)
            .OrderBy(h => h.DisplayOrder).ThenBy(h => h.Code)
            .Select(h => new HallSummary(
                h.Id, h.Code, h.Name, h.WidthM, h.DepthM,
                h.Kiosks.Count(k => k.IsActive && k.Exhibitor.IsActive),
                h.Kiosks.Where(k => k.IsActive && k.Exhibitor.IsActive).Select(k => k.ExhibitorId).Distinct().Count(),
                h.Notes))
            .ToListAsync(ct);
    }

    public async Task<HallDetail?> HallAsync(int hallId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var hall = (await HallsAsync(ct)).FirstOrDefault(h => h.Id == hallId);
        if (hall is null) return null;

        var exhibitors = await SearchExhibitorsAsync(hallId: hallId, page: page, pageSize: pageSize, ct: ct);
        int sessionCount = await db.Sessions.CountAsync(s => s.IsActive && s.HallId == hallId, ct);

        return new HallDetail(hall, exhibitors.Items, sessionCount);
    }

    // --- the programme -------------------------------------------------------

    public async Task<Page<SessionSummary>> SearchSessionsAsync(
        string? query = null,
        DateOnly? date = null,
        SessionKind? kind = null,
        int? hallId = null,
        int? categoryId = null,
        int? subCategoryId = null,
        int? visitorId = null,
        bool bookmarkedOnly = false,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        (page, pageSize) = Normalise(page, pageSize);

        await using var db = await factory.CreateDbContextAsync(ct);

        var q = db.Sessions.AsNoTracking().Where(s => s.IsActive);

        if (date is { } d) q = q.Where(s => s.EventDate == d);
        if (kind is { } k) q = q.Where(s => s.Kind == k);
        if (hallId is { } hall) q = q.Where(s => s.HallId == hall);
        if (categoryId is { } cat) q = q.Where(s => s.CategoryId == cat);
        if (subCategoryId is { } sub) q = q.Where(s => s.SubCategoryId == sub);

        if (bookmarkedOnly && visitorId is { } bv)
            q = q.Where(s => s.Bookmarks.Any(b => b.VisitorId == bv));

        string? term = Clean(query);
        if (term is not null)
        {
            q = q.Where(s =>
                EF.Functions.Like(s.Title, $"%{term}%")
                || (s.SpeakerName != null && EF.Functions.Like(s.SpeakerName, $"%{term}%"))
                || (s.SpeakerOrganisation != null && EF.Functions.Like(s.SpeakerOrganisation, $"%{term}%"))
                || (s.Abstract != null && EF.Functions.Like(s.Abstract, $"%{term}%"))
                || (s.RoomName != null && EF.Functions.Like(s.RoomName, $"%{term}%")));
        }

        int total = await q.CountAsync(ct);

        // Ordering and paging happen on the entity, not on the projected record:
        // SQL cannot sort by a column of an object it has not built yet, and
        // ordering after the Select fails to translate.
        var ordered = q
            .OrderBy(s => s.EventDate).ThenBy(s => s.StartsAt).ThenBy(s => s.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var items = await Project(ordered, visitorId).ToListAsync(ct);

        return new Page<SessionSummary>(items, total, page, pageSize);
    }

    public async Task<SessionDetail?> SessionAsync(int sessionId, int? visitorId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var one = db.Sessions.AsNoTracking().Where(s => s.IsActive && s.Id == sessionId);

        var summary = await Project(one, visitorId).FirstOrDefaultAsync(ct);
        if (summary is null) return null;

        string? body = await db.Sessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.Abstract)
            .FirstOrDefaultAsync(ct);

        return new SessionDetail(summary, body);
    }

    /// <summary>The days the programme actually covers, for the day picker.</summary>
    public async Task<IReadOnlyList<DateOnly>> SessionDatesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Sessions
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.EventDate)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);
    }

    /// <summary>Save a session to the visitor's agenda. Saving twice is not an error.</summary>
    public async Task<bool> BookmarkAsync(int visitorId, int sessionId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        if (!await db.Sessions.AnyAsync(s => s.Id == sessionId && s.IsActive, ct)) return false;

        bool exists = await db.SessionBookmarks
            .AnyAsync(b => b.VisitorId == visitorId && b.SessionId == sessionId, ct);

        if (!exists)
        {
            db.SessionBookmarks.Add(new SessionBookmark { VisitorId = visitorId, SessionId = sessionId });
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task<bool> RemoveBookmarkAsync(int visitorId, int sessionId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        int removed = await db.SessionBookmarks
            .Where(b => b.VisitorId == visitorId && b.SessionId == sessionId)
            .ExecuteDeleteAsync(ct);

        return removed > 0;
    }

    // --- one box, everything in it -------------------------------------------

    /// <summary>
    /// What the app's single search field runs. It returns a little of each kind
    /// rather than a ranked mixture, because "Siemens" should show the company
    /// and their talk as two answers to two different questions, not interleaved
    /// by a relevance score the visitor cannot see.
    /// </summary>
    public async Task<UnifiedSearchResult> SearchAllAsync(
        string query, int? visitorId, int limitPerKind = 8, CancellationToken ct = default)
    {
        // The raw term, not the LIKE-escaped one: the two filters below run in
        // memory, where "50%" is just three characters.
        string term = (query ?? "").Trim();
        if (term.Length == 0)
            return new UnifiedSearchResult([], [], [], [], 0, 0);

        var exhibitors = await SearchExhibitorsAsync(query, page: 1, pageSize: limitPerKind, ct: ct);
        var sessions = await SearchSessionsAsync(query, visitorId: visitorId, page: 1, pageSize: limitPerKind, ct: ct);

        // Categories and halls are small enough to filter in memory, and the
        // tree is wanted whole anyway.
        var tree = await CategoryTreeAsync(ct);
        var categories = tree
            .SelectMany(c => new[] { c }.Concat(c.Children))
            .Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || c.Code.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(limitPerKind)
            .ToList();

        var halls = (await HallsAsync(ct))
            .Where(h => h.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || h.Code.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(limitPerKind)
            .ToList();

        return new UnifiedSearchResult(
            exhibitors.Items, sessions.Items, categories, halls, exhibitors.Total, sessions.Total);
    }

    // --- helpers -------------------------------------------------------------

    /// <summary>
    /// Always the last step of a session query, after any filtering, ordering
    /// and paging: SQL cannot order or filter by a column of a record it has not
    /// constructed yet, and doing it the other way round fails to translate.
    /// </summary>
    private static IQueryable<SessionSummary> Project(IQueryable<ProgrammeSession> q, int? visitorId)
        => q.Select(s => new SessionSummary(
            s.Id,
            s.Code,
            s.Title,
            s.Kind.ToString(),
            s.SpeakerName,
            s.SpeakerTitle,
            s.SpeakerOrganisation,
            s.EventDate,
            s.StartsAt,
            s.EndsAt,
            s.HallId,
            s.Hall != null ? s.Hall.Name : null,
            s.RoomName,
            s.CategoryId,
            s.Category != null ? s.Category.Name : null,
            s.SubCategoryId,
            s.SubCategory != null ? s.SubCategory.Name : null,
            s.ExhibitorId,
            s.Exhibitor != null ? s.Exhibitor.CompanyName : null,
            s.RequiresBooking,
            s.Capacity,
            s.Language,
            visitorId != null && s.Bookmarks.Any(b => b.VisitorId == visitorId)));

    /// <summary>
    /// Trim the term and neutralise the LIKE wildcards, so a visitor typing
    /// "50%" gets stands with "50%" in them rather than every exhibitor at the
    /// show.
    /// </summary>
    private static string? Clean(string? query)
    {
        string trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0) return null;

        return trimmed
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]");
    }

    private static (int Page, int Size) Normalise(int page, int pageSize)
        => (page < 1 ? 1 : page, pageSize < 1 ? 25 : pageSize > MaxPageSize ? MaxPageSize : pageSize);
}
