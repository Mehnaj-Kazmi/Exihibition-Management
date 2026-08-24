using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// The meetings and lectures programme, which is what the mobile app searches
/// alongside the stands.
///
/// It is edited here rather than imported because a conference programme changes
/// during the show — a speaker misses a flight, a room is swapped at nine in the
/// morning — and the app reads this table live, so an edit made here is on every
/// visitor's phone at the next refresh.
/// </summary>
public class ProgrammeModel(IDbContextFactory<ExhibitionDbContext> factory, SettingsStore settings) : PageModel
{
    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public IReadOnlyList<SelectOption> Halls { get; private set; } = [];
    public IReadOnlyList<SelectOption> Categories { get; private set; } = [];
    public IReadOnlyList<SelectOption> SubCategories { get; private set; } = [];
    public IReadOnlyList<DateOnly> Days { get; private set; } = [];

    public DateOnly DefaultDate { get; private set; }
    public string? Message { get; private set; }
    public string? Problem { get; private set; }

    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }

    public record Row(
        int Id, string Code, string Title, SessionKind Kind, string? SpeakerName, string? SpeakerOrganisation,
        DateOnly EventDate, TimeOnly StartsAt, TimeOnly EndsAt, int? HallId, string? RoomName,
        int? CategoryId, int? SubCategoryId, int Capacity, bool RequiresBooking, string? Language,
        int Bookmarks);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostSaveAsync(
        int? id, string title, SessionKind kind, string? speakerName, string? speakerTitle,
        string? speakerOrganisation, string? summary, DateOnly eventDate, TimeOnly startsAt, TimeOnly endsAt,
        int? hallId, string? roomName, int? exhibitorId, int? categoryId, int? subCategoryId,
        int capacity, bool requiresBooking, string? language, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        title = (title ?? "").Trim();
        if (title.Length == 0) return await FailAsync("A session needs a title.", ct);

        if (endsAt <= startsAt)
            return await FailAsync($"'{title}' ends at or before it starts. Check the times.", ct);

        // A room cannot hold two things at once, and a visitor who walks to the
        // wrong one has lost the slot. Better to refuse the save than to publish
        // a programme that cannot physically happen.
        if (!string.IsNullOrWhiteSpace(roomName))
        {
            string room = roomName.Trim();
            var clash = await db.Sessions
                .Where(s => s.IsActive && s.Id != id && s.EventDate == eventDate && s.RoomName == room)
                .Where(s => s.StartsAt < endsAt && startsAt < s.EndsAt)
                .Select(s => new { s.Title, s.StartsAt, s.EndsAt })
                .FirstOrDefaultAsync(ct);

            if (clash is not null)
                return await FailAsync(
                    $"{room} is already taken on {eventDate:ddd d MMM} from {clash.StartsAt:HH\\:mm} to "
                    + $"{clash.EndsAt:HH\\:mm} by '{clash.Title}'.", ct);
        }

        // A sub-category that is not a child of the chosen category would make
        // the app's filters disagree with the admin console.
        if (subCategoryId is { } sub)
        {
            int? parentId = await db.Categories
                .Where(c => c.Id == sub)
                .Select(c => c.ParentId)
                .FirstOrDefaultAsync(ct);

            if (categoryId is null || parentId != categoryId)
                return await FailAsync("That sub-category does not belong to the category chosen.", ct);
        }

        ProgrammeSession session;
        if (id is null)
        {
            session = new ProgrammeSession { Code = await NextCodeAsync(db, ct) };
            db.Sessions.Add(session);
        }
        else
        {
            session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct)
                      ?? throw new InvalidOperationException($"no session {id}");
        }

        session.Title = title;
        session.Kind = kind;
        session.SpeakerName = Clean(speakerName);
        session.SpeakerTitle = Clean(speakerTitle);
        session.SpeakerOrganisation = Clean(speakerOrganisation);
        session.Abstract = Clean(summary);
        session.EventDate = eventDate;
        session.StartsAt = startsAt;
        session.EndsAt = endsAt;
        session.HallId = hallId;
        session.RoomName = Clean(roomName);
        session.ExhibitorId = exhibitorId;
        session.CategoryId = categoryId;
        session.SubCategoryId = subCategoryId;
        session.Capacity = capacity < 0 ? 0 : capacity;
        session.RequiresBooking = requiresBooking;
        session.Language = Clean(language);
        session.IsActive = true;
        session.UpdatedUtc = DateTime.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Action = id is null ? "session.create" : "session.update",
            EntityName = "ProgrammeSession",
            EntityId = session.Id.ToString(),
            User = User.Identity?.Name,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { title, kind, eventDate, startsAt, roomName }),
        });

        await db.SaveChangesAsync(ct);

        TempData["message"] = $"'{title}' saved. It is on visitors' phones from their next refresh.";
        return RedirectToPage(new { date = eventDate });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return RedirectToPage();

        // Retired rather than deleted, because visitors have it in their agenda
        // and the bookmark rows point at it.
        session.IsActive = false;
        session.UpdatedUtc = DateTime.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Action = "session.retire",
            EntityName = "ProgrammeSession",
            EntityId = session.Id.ToString(),
            User = User.Identity?.Name,
        });

        await db.SaveChangesAsync(ct);

        int saved = await db.SessionBookmarks.CountAsync(b => b.SessionId == id, ct);
        TempData["message"] = saved > 0
            ? $"'{session.Title}' removed from the programme. It was in {saved} visitor agenda(s), "
              + "and will disappear from them."
            : $"'{session.Title}' removed from the programme.";

        return RedirectToPage(new { date = session.EventDate });
    }

    private async Task<IActionResult> FailAsync(string message, CancellationToken ct)
    {
        Problem = message;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Message ??= TempData["message"] as string;
        Problem ??= TempData["problem"] as string;

        await using var db = await factory.CreateDbContextAsync(ct);

        Days = await db.Sessions.AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.EventDate)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

        // Default to a day that has something on it, so the screen is not empty
        // on the morning of the second day just because "today" has no rows yet.
        var today = Services.TrackingRuntime.LocalDate(settings.Current.Exhibition);
        DefaultDate = Days.Contains(today) ? today : Days.FirstOrDefault(d => d >= today, Days.FirstOrDefault(today));

        var shown = Date ?? DefaultDate;

        var bookmarkCounts = await db.SessionBookmarks.AsNoTracking()
            .GroupBy(b => b.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count, ct);

        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.IsActive && s.EventDate == shown)
            .OrderBy(s => s.StartsAt).ThenBy(s => s.RoomName)
            .Select(s => new
            {
                s.Id, s.Code, s.Title, s.Kind, s.SpeakerName, s.SpeakerOrganisation,
                s.EventDate, s.StartsAt, s.EndsAt, s.HallId, s.RoomName,
                s.CategoryId, s.SubCategoryId, s.Capacity, s.RequiresBooking, s.Language,
            })
            .ToListAsync(ct);

        Rows = rows
            .Select(s => new Row(
                s.Id, s.Code, s.Title, s.Kind, s.SpeakerName, s.SpeakerOrganisation,
                s.EventDate, s.StartsAt, s.EndsAt, s.HallId, s.RoomName,
                s.CategoryId, s.SubCategoryId, s.Capacity, s.RequiresBooking, s.Language,
                bookmarkCounts.GetValueOrDefault(s.Id)))
            .ToList();

        Halls = await db.Halls.AsNoTracking()
            .Where(h => h.IsActive)
            .OrderBy(h => h.DisplayOrder)
            .Select(h => new SelectOption(h.Id, h.Name))
            .ToListAsync(ct);

        Categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.ParentId == null)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new SelectOption(c.Id, c.Name))
            .ToListAsync(ct);

        SubCategories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.ParentId != null)
            .OrderBy(c => c.Parent!.DisplayOrder).ThenBy(c => c.DisplayOrder)
            .Select(c => new SelectOption(c.Id, c.Parent!.Name + " › " + c.Name))
            .ToListAsync(ct);
    }

    private static async Task<string> NextCodeAsync(ExhibitionDbContext db, CancellationToken ct)
    {
        int n = await db.Sessions.CountAsync(ct);
        string code;
        do { code = $"S{++n:D4}"; }
        while (await db.Sessions.AnyAsync(s => s.Code == code, ct));
        return code;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
