using Exb.Data;
using Exb.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Reports;

public class ViewModel(IDbContextFactory<ExhibitionDbContext> factory) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public DailyReport? Report { get; private set; }
    public string VisitorName { get; private set; } = "";
    public string VisitorEmail { get; private set; } = "";
    public int VisitorId { get; private set; }
    public OutboxEmail? Email { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        Report = await db.DailyReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == Id, ct);
        if (Report is null) return RedirectToPage("/Reports/Index");

        var visitor = await db.Visitors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == Report.VisitorId, ct);
        VisitorName = visitor?.FullName ?? "";
        VisitorEmail = visitor?.Email ?? "";
        VisitorId = Report.VisitorId;

        if (Report.OutboxEmailId is { } emailId)
            Email = await db.OutboxEmails.AsNoTracking().FirstOrDefaultAsync(m => m.Id == emailId, ct);

        return Page();
    }

    /// <summary>
    /// Serves the stored report as its own document.
    ///
    /// It is rendered in a sandboxed iframe rather than inlined into this page:
    /// the report is a complete HTML document with its own layout, and dropping
    /// it into the admin console would let its styles fight with the console's.
    /// </summary>
    public async Task<IActionResult> OnGetRawAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var report = await db.DailyReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == Id, ct);
        if (report is null) return NotFound();

        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";
        return Content(report.Html, "text/html; charset=utf-8");
    }
}
