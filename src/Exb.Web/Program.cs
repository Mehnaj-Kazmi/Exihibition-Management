using Exb.Core.Qr;
using Exb.Data;
using Exb.Data.Seed;
using Exb.Data.Services;
using Exb.Web.Api;
using Exb.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- database ---------------------------------------------------------------
// A context factory rather than a scoped context: the tracking, mail and
// end-of-day background services all need their own short-lived contexts, and
// none of them live inside a request scope.
// The environment variable wins, so a deployment can point at its own server
// without editing a file that is under source control.
string connectionString =
    Environment.GetEnvironmentVariable("EXB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("ExhibitionDb")
    ?? throw new InvalidOperationException(
        "No database configured. Set the EXB_CONNECTION environment variable, "
        + "or ConnectionStrings:ExhibitionDb in appsettings.json.");

builder.Services.AddDbContextFactory<ExhibitionDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        // -2 is the client-side timeout SQL Server raises when it accepts a
        // connection but is too busy to finish establishing it, and 1205 is a
        // deadlock victim. Neither is in the provider's default transient list,
        // yet both are exactly the sort of momentary pressure a show day
        // produces once tracking has written a few hundred thousand visits.
        // Left unretried they surface to a visitor as "cannot reach the
        // exhibition system" while the database is merely slow, not down.
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: [-2, 1205]);
        sql.CommandTimeout(120);
    }));

// --- application services ---------------------------------------------------
// These are singletons because they hold no state beyond the context factory,
// and the background services need them outside any request scope.
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<FacilityProvider>();
builder.Services.AddSingleton<BadgeDirectory>();
builder.Services.AddSingleton<VisitRepository>();
builder.Services.AddSingleton<InterestQueryService>();
builder.Services.AddSingleton<FormSchemaService>();
builder.Services.AddSingleton<RegistrationService>();
builder.Services.AddSingleton<CatalogueRequestService>();
builder.Services.AddSingleton<MobileDirectoryService>();
builder.Services.AddSingleton<MobileAuthService>();
builder.Services.AddSingleton<MailQueue>();
builder.Services.AddSingleton<EndOfDayService>();
builder.Services.AddSingleton<DemoSeeder>();
builder.Services.AddSingleton<TrackingRuntime>();
builder.Services.AddSingleton<ITransferProviderSelector, TransferProviderSelector>();
builder.Services.AddSingleton<IMailTransportSelector, MailTransportSelector>();

builder.Services.AddSingleton(_ => new CatalogueStorage(
    Path.IsPathRooted(builder.Configuration["Storage:Root"] ?? "App_Data")
        ? builder.Configuration["Storage:Root"]!
        : Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Storage:Root"] ?? "App_Data")));

builder.Services.AddHttpClient();

builder.Services.AddHostedService<TrackingHostedService>();
builder.Services.AddHostedService<MailDispatchHostedService>();
builder.Services.AddHostedService<EndOfDayHostedService>();

builder.Services.AddRazorPages();

// The mobile companion is a separate app (native, or a Blazor WebAssembly
// client running in its own browser origin) calling the versioned API from
// outside this site's own origin. Scoped to /api/v1 only, and to Development
// here because the shipped default has no admin auth either — a production
// deployment should list its real mobile-client origin(s) explicitly.
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileApp", policy =>
    {
        if (builder.Environment.IsDevelopment())
            policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod();
    });
});

// Naming policy pinned explicitly rather than relying on the protocol default,
// because the floor plan's JavaScript reads these field names directly.
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

var app = builder.Build();

// --- first-run setup --------------------------------------------------------
await InitialiseAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors("MobileApp");
app.MapRazorPages();
app.MapHub<LiveHub>("/hubs/live");

// --- the mobile app's API ---------------------------------------------------
// Everything the Android and iOS apps talk to, behind one versioned prefix.
app.MapMobileApi();

// --- QR images for stand signage -------------------------------------------
// Generated on demand rather than stored: they are a pure function of the
// stand's token and the public base URL, and caching them would only create
// something to go stale when the venue's hostname changes.
app.MapGet("/qr/{token}.svg", (string token, SettingsStore settings) =>
{
    string url = ScanUrl(settings, token);
    return Results.Text(QrCode.Encode(url).ToSvg(), "image/svg+xml");
});

app.MapGet("/qr/{token}.png", (string token, SettingsStore settings, int scale = 8) =>
{
    string url = ScanUrl(settings, token);
    byte[] png = QrCode.Encode(url).ToPng(Math.Clamp(scale, 2, 24));
    return Results.File(png, "image/png");
});

// --- e-catalogue pack download ---------------------------------------------
// The token is the only credential on this link, so it is checked against the
// delivery job and honours the expiry that the visitor was told about.
app.MapGet("/d/{token}", async (
    string token,
    IDbContextFactory<ExhibitionDbContext> factory,
    CatalogueStorage storage,
    SettingsStore settings) =>
{
    await using var db = await factory.CreateDbContextAsync();
    var job = await db.DeliveryJobs
        .AsNoTracking()
        .FirstOrDefaultAsync(j => j.DownloadToken == token && j.ZipPath != null);

    if (job is null) return Results.NotFound("That download link is not valid.");
    if (job.TransferExpiresUtc is { } expiry && expiry < DateTime.UtcNow)
        return Results.Text("That download link has expired. Contact the organiser for a new one.", "text/plain", null, 410);

    string? path = storage.ResolveStored(job.ZipPath!);
    if (path is null || !File.Exists(path)) return Results.NotFound("The pack file is no longer on the server.");

    string name = $"{Slug(settings.Current.Exhibition.Name)}-catalogues-{job.EventDate:yyyy-MM-dd}.zip";
    return Results.File(path, "application/zip", name);
});

app.Run();
return;

static string ScanUrl(SettingsStore settings, string token)
    => $"{settings.Current.Exhibition.PublicBaseUrl.TrimEnd('/')}/s/{token}";

static string Slug(string value)
{
    var chars = value.ToLowerInvariant()
        .Select(c => char.IsLetterOrDigit(c) ? c : '-')
        .ToArray();
    return new string(chars).Trim('-').Replace("--", "-");
}

/// <summary>
/// Bring the database up to date and make sure the system has everything it
/// needs to be usable on first launch: default settings, the built-in form
/// layouts, and — on a genuinely empty database — a demonstration exhibition.
/// </summary>
static async Task InitialiseAsync(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var factory = app.Services.GetRequiredService<IDbContextFactory<ExhibitionDbContext>>();

    try
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        logger.LogInformation("Database schema is up to date.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex,
            "Could not reach or migrate the SQL Server database. Check the 'ExhibitionDb' connection string.");
        throw;
    }

    await app.Services.GetRequiredService<SettingsStore>().EnsureDefaultsAsync();
    await app.Services.GetRequiredService<FormSchemaService>().EnsureDefaultsAsync();

    if (app.Configuration.GetValue("Seed:DemoExhibition", true))
        await app.Services.GetRequiredService<DemoSeeder>().SeedIfEmptyAsync();

    await app.Services.GetRequiredService<FacilityProvider>().RebuildAsync();
}

/// <summary>Named so the tests can reference the web host.</summary>
public partial class Program;
