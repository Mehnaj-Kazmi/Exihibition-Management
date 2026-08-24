using Exb.Core.Interest;
using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Exb.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Api;

// --- request bodies ----------------------------------------------------------

public sealed record RequestCodeBody(string Email);

public sealed record VerifyCodeBody(
    string Email, string Code, string? Platform, string? DeviceName, string? AppVersion);

public sealed record ConsentBody(bool? ConsentEmail, bool? ConsentTracking);

public sealed record ScanBody(string Token);

public sealed record CatalogueBody(int KioskId);

public sealed record IncludeBody(bool Included);

/// <summary>
/// The mobile app's HTTP surface.
///
/// It is deliberately a separate, versioned prefix rather than JSON bolted onto
/// the Razor pages: the admin console is free to change its screens without
/// breaking an app that is already on somebody's phone in a hall, and everything
/// a visitor's phone can reach is visible in one file, which is the right
/// property for the part of the system that is exposed to the public network.
///
/// Authentication is a bearer token from <see cref="MobileAuthService"/>. There
/// is no cookie here on purpose — a mobile client has no business carrying the
/// admin console's cookies, and a token-only API cannot be driven by a browser
/// that happens to have an admin session open.
/// </summary>
public static class MobileApi
{
    public static void MapMobileApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireCors("MobileApp");

        MapAuth(api);
        MapDirectory(api);
        MapProgramme(api);
        MapVisitor(api);
    }

    // --- signing in ----------------------------------------------------------

    private static void MapAuth(RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapPost("/request-code", async (
            RequestCodeBody body, MobileAuthService service, HttpContext http, CancellationToken ct) =>
        {
            var result = await service.RequestCodeAsync(
                body.Email, http.Connection.RemoteIpAddress?.ToString(), ct);

            if (result.Outcome == LoginCodeOutcome.RateLimited)
            {
                return Results.Json(new
                {
                    sent = false,
                    message = "Too many codes requested. Try again in a few minutes, "
                            + "or ask at the registration desk.",
                }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            // An unregistered address is answered exactly like a registered one.
            // Anything else turns this endpoint into a way of testing whether a
            // given person is attending the show.
            return Results.Ok(new
            {
                sent = true,
                expiresInSeconds = result.ExpiresInSeconds,
                message = "If that address is registered, a six-digit code is on its way to it.",
                developmentCode = result.DevelopmentCode,
            });
        });

        auth.MapPost("/verify", async (VerifyCodeBody body, MobileAuthService service, CancellationToken ct) =>
        {
            var result = await service.VerifyAsync(
                body.Email, body.Code, body.Platform, body.DeviceName, body.AppVersion, ct);

            return result.Outcome switch
            {
                VerifyOutcome.Success => Results.Ok(new
                {
                    token = result.Token,
                    expiresUtc = result.ExpiresUtc,
                    visitor = result.Identity,
                }),
                VerifyOutcome.Expired => Problem(
                    StatusCodes.Status401Unauthorized,
                    "That code has expired. Ask for a new one."),
                VerifyOutcome.TooManyAttempts => Problem(
                    StatusCodes.Status429TooManyRequests,
                    "That code has been entered incorrectly too many times. Ask for a new one."),
                _ => Problem(StatusCodes.Status401Unauthorized, "That code is not right."),
            };
        });

        auth.MapPost("/logout", async (MobileAuthService service, HttpContext http, CancellationToken ct) =>
        {
            await service.RevokeAsync(BearerToken(http), ct);
            return Results.Ok(new { signedOut = true });
        });
    }

    // --- exhibitors, categories, halls ---------------------------------------

    private static void MapDirectory(RouteGroupBuilder api)
    {
        // Browsing the catalogue needs a signed-in visitor like everything else,
        // so that an exhibitor list scraper has to get past the email check
        // first — and so scan state can be shown inline on every screen.
        var group = api.MapGroup("").RequireVisitor();

        group.MapGet("/exhibition", async (
            SettingsStore settings, MobileDirectoryService directory, CancellationToken ct) =>
        {
            var exhibition = settings.Current.Exhibition;
            return Results.Ok(new
            {
                name = exhibition.Name,
                edition = exhibition.Edition,
                venue = exhibition.Venue,
                organiser = exhibition.OrganiserName,
                organiserEmail = exhibition.OrganiserEmail,
                today = TrackingRuntime.LocalDate(exhibition),
                halls = await directory.HallsAsync(ct),
                categories = await directory.CategoryTreeAsync(ct),
                countries = await directory.CountriesAsync(ct),
                programmeDates = await directory.SessionDatesAsync(ct),
            });
        });

        group.MapGet("/categories", (MobileDirectoryService directory, CancellationToken ct)
            => directory.CategoryTreeAsync(ct));

        group.MapGet("/halls", (MobileDirectoryService directory, CancellationToken ct)
            => directory.HallsAsync(ct));

        // Paging parameters are nullable with defaults applied here rather than
        // plain ints: a minimal API rejects a request that omits a
        // non-nullable value type outright, and "page" is exactly the sort of
        // parameter a caller leaves off when it wants the first one.
        group.MapGet("/halls/{id:int}", async (
            int id, int? page, int? pageSize, MobileDirectoryService directory, CancellationToken ct) =>
        {
            var hall = await directory.HallAsync(id, Page(page), Size(pageSize, 50), ct);
            return hall is null ? Results.NotFound() : Results.Ok(hall);
        });

        group.MapGet("/exhibitors", (
            string? q, int? categoryId, int? subCategoryId, int? hallId, string? country,
            int? page, int? pageSize, MobileDirectoryService directory, CancellationToken ct)
            => directory.SearchExhibitorsAsync(
                q, categoryId, subCategoryId, hallId, country,
                Page(page), Size(pageSize, 25), ct));

        group.MapGet("/exhibitors/{id:int}", async (
            int id, HttpContext http, MobileDirectoryService directory, SettingsStore settings,
            CancellationToken ct) =>
        {
            var me = http.Visitor();
            var day = TrackingRuntime.LocalDate(settings.Current.Exhibition);

            var detail = await directory.ExhibitorAsync(id, me.VisitorId, day, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapGet("/search", (
            string? q, HttpContext http, MobileDirectoryService directory, CancellationToken ct)
            => directory.SearchAllAsync(q ?? "", http.Visitor().VisitorId, ct: ct));
    }

    // --- meetings and lectures -----------------------------------------------

    private static void MapProgramme(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/sessions").RequireVisitor();

        group.MapGet("", (
            string? q, DateOnly? date, string? kind, int? hallId, int? categoryId, int? subCategoryId,
            bool? bookmarked, int? page, int? pageSize,
            HttpContext http, MobileDirectoryService directory, CancellationToken ct) =>
        {
            SessionKind? parsed = Enum.TryParse<SessionKind>(kind, ignoreCase: true, out var value) ? value : null;

            return directory.SearchSessionsAsync(
                q, date, parsed, hallId, categoryId, subCategoryId,
                http.Visitor().VisitorId, bookmarked ?? false,
                Page(page), Size(pageSize, 50), ct);
        });

        group.MapGet("/dates", (MobileDirectoryService directory, CancellationToken ct)
            => directory.SessionDatesAsync(ct));

        group.MapGet("/{id:int}", async (
            int id, HttpContext http, MobileDirectoryService directory, CancellationToken ct) =>
        {
            var detail = await directory.SessionAsync(id, http.Visitor().VisitorId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPost("/{id:int}/bookmark", async (
            int id, HttpContext http, MobileDirectoryService directory, CancellationToken ct) =>
        {
            bool ok = await directory.BookmarkAsync(http.Visitor().VisitorId, id, ct);
            return ok ? Results.Ok(new { bookmarked = true }) : Results.NotFound();
        });

        group.MapDelete("/{id:int}/bookmark", async (
            int id, HttpContext http, MobileDirectoryService directory, CancellationToken ct) =>
        {
            await directory.RemoveBookmarkAsync(http.Visitor().VisitorId, id, ct);
            return Results.Ok(new { bookmarked = false });
        });
    }

    // --- the visitor's own day -----------------------------------------------

    private static void MapVisitor(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/me").RequireVisitor();

        group.MapGet("", (HttpContext http) => Results.Ok(http.Visitor()));

        group.MapPatch("/consent", async (
            ConsentBody body, HttpContext http, MobileAuthService service, CancellationToken ct) =>
        {
            var updated = await service.UpdateConsentAsync(
                http.Visitor().VisitorId, body.ConsentEmail, body.ConsentTracking, ct);

            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapGet("/agenda", (
            HttpContext http, MobileDirectoryService directory, CancellationToken ct)
            => directory.SearchSessionsAsync(
                visitorId: http.Visitor().VisitorId, bookmarkedOnly: true, pageSize: 200, ct: ct));

        // --- e-catalogue requests --------------------------------------------

        group.MapGet("/catalogues", async (
            HttpContext http, CatalogueRequestService catalogues, SettingsStore settings, CancellationToken ct) =>
        {
            var day = TrackingRuntime.LocalDate(settings.Current.Exhibition);
            var items = await catalogues.TodayForVisitorAsync(http.Visitor().VisitorId, day, ct);
            return Results.Ok(new { eventDate = day, items });
        });

        // Requesting a catalogue from an exhibitor's page rather than by
        // scanning at the stand. It resolves the stand's own QR token and goes
        // through exactly the same path a scan does, so a visitor who does both
        // gets one entry in their pack rather than two.
        group.MapPost("/catalogues", async (
            CatalogueBody body, HttpContext http, IDbContextFactory<ExhibitionDbContext> factory,
            CatalogueRequestService catalogues, SettingsStore settings, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);

            string? token = await db.Kiosks
                .AsNoTracking()
                .Where(k => k.Id == body.KioskId && k.IsActive && k.Exhibitor.IsActive)
                .Select(k => k.QrToken)
                .FirstOrDefaultAsync(ct);

            if (token is null) return Problem(StatusCodes.Status404NotFound, "That stand is not at this exhibition.");

            var day = TrackingRuntime.LocalDate(settings.Current.Exhibition);
            var result = await catalogues.RecordAsync(token, http.Visitor().VisitorId, day, "app", ct);

            return Results.Ok(new
            {
                outcome = result.Outcome == ScanOutcome.AlreadyRequested ? "alreadyRequested" : "added",
                stand = result.Target,
                todayCount = result.TodayCount,
            });
        });

        group.MapPatch("/catalogues/{kioskId:int}", async (
            int kioskId, IncludeBody body, HttpContext http,
            CatalogueRequestService catalogues, SettingsStore settings, CancellationToken ct) =>
        {
            var day = TrackingRuntime.LocalDate(settings.Current.Exhibition);
            bool ok = await catalogues.SetIncludedAsync(
                http.Visitor().VisitorId, kioskId, day, body.Included, ct);

            return ok ? Results.Ok(new { included = body.Included }) : Results.NotFound();
        });

        group.MapGet("/day", async (
            HttpContext http, InterestQueryService interest, SettingsStore settings, CancellationToken ct) =>
        {
            var me = http.Visitor();
            var day = TrackingRuntime.LocalDate(settings.Current.Exhibition);

            if (!me.ConsentTracking)
            {
                // Saying "you have no visits" to somebody who opted out would be
                // misleading; the app shows the real reason instead.
                return Results.Ok(new
                {
                    eventDate = day,
                    trackingConsent = false,
                    message = "Stand tracking is switched off for your badge, so there is nothing to show here. "
                            + "You can turn it on under Profile.",
                });
            }

            var profile = await interest.ProfileAsync(me.VisitorId, day, ct);
            return Results.Ok(new { eventDate = day, trackingConsent = true, day = Describe(profile) });
        });

        // --- scanning a stand's QR code --------------------------------------

        group.MapPost("/scan", async (
            ScanBody body, HttpContext http, CatalogueRequestService catalogues,
            SettingsStore settings, CancellationToken ct) =>
        {
            var day = TrackingRuntime.LocalDate(settings.Current.Exhibition);
            string token = CatalogueRequestService.NormaliseScannedValue(body.Token);

            var result = await catalogues.RecordAsync(token, http.Visitor().VisitorId, day, "app", ct);

            return result.Outcome switch
            {
                ScanOutcome.Added => Results.Ok(new
                {
                    outcome = "added",
                    message = $"{result.Target!.ExhibitorName} added. It will be in tonight's pack.",
                    stand = result.Target,
                    todayCount = result.TodayCount,
                }),
                ScanOutcome.AlreadyRequested => Results.Ok(new
                {
                    outcome = "alreadyRequested",
                    message = $"{result.Target!.ExhibitorName} is already on your list.",
                    stand = result.Target,
                    todayCount = result.TodayCount,
                }),
                _ => Problem(
                    StatusCodes.Status404NotFound,
                    "That code is not a stand at this exhibition. Check you scanned the code on the stand itself."),
            };
        });
    }

    // --- helpers -------------------------------------------------------------

    /// <summary>
    /// The day profile, flattened. The analyser's own records carry computed
    /// members that System.Text.Json would not serialise, and the app should not
    /// have to reimplement "4 min 20 s" formatting to agree with the email the
    /// same visitor gets that evening.
    /// </summary>
    private static object Describe(VisitorDayProfile p) => new
    {
        totalDwellSeconds = p.TotalDwellSeconds,
        totalDwellText = p.TotalDwellText,
        standsWithInterest = p.StandsWithInterest,
        passedBy = p.PassedBy,
        visited = p.Visited.Select(v => new
        {
            exhibitorId = v.Kiosk.ExhibitorId,
            exhibitorName = v.Kiosk.ExhibitorName,
            standNumber = v.Kiosk.StandNumber,
            hallName = v.Kiosk.HallName,
            location = v.Kiosk.Location,
            categoryName = v.Kiosk.CategoryName,
            dwellSeconds = v.TotalDwellSeconds,
            dwellText = v.DwellText,
            level = v.Level.ToString(),
            levelText = InterestFormatting.LevelText(v.Level),
            visitCount = v.VisitCount,
            catalogueRequested = v.CatalogueRequested,
        }),
        categories = p.Categories.Select(c => new
        {
            categoryId = c.CategoryId,
            categoryName = c.CategoryName,
            dwellSeconds = c.TotalDwellSeconds,
            dwellText = c.DwellText,
            standCount = c.StandCount,
            sharePct = Math.Round(c.SharePct, 1),
            bestLevel = c.BestLevel.ToString(),
        }),
        missed = p.Missed.Select(m => new
        {
            exhibitorId = m.Kiosk.ExhibitorId,
            exhibitorName = m.Kiosk.ExhibitorName,
            standNumber = m.Kiosk.StandNumber,
            hallName = m.Kiosk.HallName,
            location = m.Kiosk.Location,
            categoryName = m.Kiosk.CategoryName,
            website = m.Kiosk.Website,
            reason = m.Reason,
        }),
    };

    private static int Page(int? page) => page is null or < 1 ? 1 : page.Value;

    private static int Size(int? pageSize, int fallback)
        => pageSize is null or < 1 ? fallback : pageSize.Value;

    internal static string? BearerToken(HttpContext http)
    {
        string? header = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;

        const string scheme = "Bearer ";
        return header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? header[scheme.Length..].Trim()
            : null;
    }

    private static IResult Problem(int status, string detail)
        => Results.Json(new { error = detail }, statusCode: status);
}
