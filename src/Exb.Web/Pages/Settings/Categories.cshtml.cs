using Exb.Data;
using Exb.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Pages.Settings;

/// <summary>
/// The product taxonomy. It is what interest is rolled up into, and what the
/// missed-stand engine matches on, so it is worth getting right before
/// exhibitors are entered.
/// </summary>
public class CategoriesModel(IDbContextFactory<ExhibitionDbContext> factory) : PageModel
{
    public IReadOnlyList<Node> Tree { get; private set; } = [];
    public IReadOnlyList<(int Id, string Name)> TopLevel { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Problem { get; private set; }

    public record Node(int Id, string Code, string Name, string? Colour, int DisplayOrder, int Exhibitors, IReadOnlyList<Node> Children);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostSaveAsync(
        int? id, string code, string name, int? parentId, string? colour, int displayOrder, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        code = (code ?? "").Trim().ToUpperInvariant();
        name = (name ?? "").Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            TempData["problem"] = "A category needs both a code and a name.";
            return RedirectToPage();
        }

        if (await db.Categories.AnyAsync(c => c.Code == code && c.Id != id, ct))
        {
            TempData["problem"] = $"Category code '{code}' is already in use.";
            return RedirectToPage();
        }

        Category category;
        if (id is null)
        {
            category = new Category();
            db.Categories.Add(category);
        }
        else
        {
            category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new InvalidOperationException($"no category {id}");

            // Only two levels are supported, and the reports assume it: a
            // sub-category that acquired children would silently disappear from
            // the rollups.
            if (parentId is not null && await db.Categories.AnyAsync(c => c.ParentId == category.Id, ct))
            {
                TempData["problem"] = $"'{category.Name}' has sub-categories, so it cannot itself become one.";
                return RedirectToPage();
            }

            if (parentId == category.Id)
            {
                TempData["problem"] = "A category cannot be its own parent.";
                return RedirectToPage();
            }
        }

        category.Code = code;
        category.Name = name;
        category.ParentId = parentId;
        category.Colour = string.IsNullOrWhiteSpace(colour) ? null : colour;
        category.DisplayOrder = displayOrder;
        category.IsActive = true;

        await db.SaveChangesAsync(ct);
        TempData["message"] = $"'{category.Name}' saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return RedirectToPage();

        int exhibitors = await db.Exhibitors.CountAsync(e => e.CategoryId == id || e.SubCategoryId == id, ct);
        if (exhibitors > 0)
        {
            TempData["problem"] =
                $"{exhibitors} exhibitor(s) are classified under '{category.Name}'. Reclassify them first, "
                + "or their visitors' interest reports would lose their category.";
            return RedirectToPage();
        }

        if (await db.Categories.AnyAsync(c => c.ParentId == id, ct))
        {
            TempData["problem"] = $"'{category.Name}' still has sub-categories. Remove those first.";
            return RedirectToPage();
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        TempData["message"] = $"'{category.Name}' removed.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Message = TempData["message"] as string;
        Problem = TempData["problem"] as string;

        await using var db = await factory.CreateDbContextAsync(ct);

        var all = await db.Categories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var counts = await db.Exhibitors.AsNoTracking()
            .Where(e => e.IsActive)
            .GroupBy(e => e.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key ?? 0, x => x.Count, ct);

        var subCounts = await db.Exhibitors.AsNoTracking()
            .Where(e => e.IsActive && e.SubCategoryId != null)
            .GroupBy(e => e.SubCategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key ?? 0, x => x.Count, ct);

        Tree = all.Where(c => c.ParentId is null)
            .Select(c => new Node(
                c.Id, c.Code, c.Name, c.Colour, c.DisplayOrder, counts.GetValueOrDefault(c.Id),
                all.Where(s => s.ParentId == c.Id)
                   .Select(s => new Node(s.Id, s.Code, s.Name, s.Colour, s.DisplayOrder, subCounts.GetValueOrDefault(s.Id), []))
                   .ToList()))
            .ToList();

        TopLevel = all.Where(c => c.ParentId is null).Select(c => (c.Id, c.Name)).ToList();
    }
}
