using System.IO.Compression;
using System.Text;
using Exb.Core.Configuration;
using Exb.Core.Delivery;
using Exb.Core.Dwell;
using Exb.Core.Interest;
using Exb.Core.Packaging;
using Exb.Core.Reports;
using Exb.Core.Text;
using Xunit;

namespace Exb.Tests;

public class CataloguePackTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "exb-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public CataloguePackTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(_workspace, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static PackItem Item(string company, string stand, params PackFile[] files) => new(
        ExhibitorId: company.GetHashCode() & 0x7FFF,
        ExhibitorName: company,
        StandNumber: stand,
        HallName: "Hall 1",
        CategoryName: "Textile Machinery",
        SubCategoryName: "Weaving",
        Website: "www.example.com",
        Email: "sales@example.com",
        Summary: "Looms and spare parts",
        RequestedUtc: new DateTime(2026, 8, 17, 12, 30, 0, DateTimeKind.Utc),
        Files: files);

    [Fact]
    public void BuildsAReadableZipWithAnIndexAndEveryCatalogueFile()
    {
        string pdf = WriteFile("brochure.pdf", "%PDF-1.4 pretend brochure");
        string zipPath = Path.Combine(_workspace, "pack.zip");

        var result = new CataloguePackBuilder().Build(
            zipPath, "Sara Khan", "SMA Tech Expo", new DateOnly(2026, 8, 17),
            [Item("Meridian Systems", "H1-004", new PackFile("brochure.pdf", "application/pdf", pdf))]);

        Assert.True(File.Exists(zipPath));
        Assert.True(result.SizeBytes > 0);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(1, result.FileCount);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("index.html", names);
        Assert.Contains("README.txt", names);
        Assert.Contains(names, n => n.EndsWith("brochure.pdf"));

        using var reader = new StreamReader(zip.GetEntry("index.html")!.Open());
        string index = reader.ReadToEnd();
        Assert.Contains("Meridian Systems", index);
        Assert.Contains("H1-004", index);
        Assert.Contains("Sara Khan", index);
    }

    [Fact]
    public void AnExhibitorWithNoCatalogueStillGetsAFolderWithTheirDetails()
    {
        string zipPath = Path.Combine(_workspace, "pack.zip");

        var result = new CataloguePackBuilder().Build(
            zipPath, "Visitor", "Expo", new DateOnly(2026, 8, 17),
            [Item("Quiet Exhibitor", "H2-011")]);

        using var zip = ZipFile.OpenRead(zipPath);

        Assert.Contains(zip.Entries, e => e.FullName.Contains("stand details"));
        Assert.Contains(result.Warnings, w => w.Contains("has not published an e-catalogue"));
    }

    [Fact]
    public void AMissingSourceFileIsReportedRatherThanSilentlyDroppingTheExhibitor()
    {
        string zipPath = Path.Combine(_workspace, "pack.zip");
        string vanished = Path.Combine(_workspace, "gone.pdf");

        var result = new CataloguePackBuilder().Build(
            zipPath, "Visitor", "Expo", new DateOnly(2026, 8, 17),
            [Item("Ghost Systems", "H1-009", new PackFile("gone.pdf", "application/pdf", vanished))]);

        Assert.Contains(result.Warnings, w => w.Contains("missing from storage"));

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName.Contains("Ghost Systems"));
    }

    [Fact]
    public void ExhibitorNamesWithAwkwardCharactersDoNotEscapeTheirFolder()
    {
        string zipPath = Path.Combine(_workspace, "pack.zip");

        new CataloguePackBuilder().Build(
            zipPath, "Visitor", "Expo", new DateOnly(2026, 8, 17),
            [Item("../../etc/passwd & \"Co\"", "H1-001")]);

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            Assert.DoesNotContain("..", entry.FullName);
            Assert.False(Path.IsPathRooted(entry.FullName));
        }
    }

    [Fact]
    public void IndexHtmlEscapesExhibitorSuppliedText()
    {
        string zipPath = Path.Combine(_workspace, "pack.zip");

        new CataloguePackBuilder().Build(
            zipPath, "Visitor", "Expo", new DateOnly(2026, 8, 17),
            [Item("<script>alert(1)</script>", "H1-001")]);

        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry("index.html")!.Open());
        string index = reader.ReadToEnd();

        Assert.DoesNotContain("<script>alert(1)</script>", index);
        Assert.Contains("&lt;script&gt;", index);
    }
}

public class DailyReportTests
{
    private static readonly DateOnly Day = new(2026, 8, 17);

    private static KioskFact Stand(int id, string name, string? category = "Textile Machinery") => new(
        id, $"H1-{id:D3}", 1, "H1", "Hall 1", "D7", id, name,
        1, category, 11, "Weaving", "www.example.com", "Looms and spares", "Pakistan", $"TOKEN{id}");

    private static VisitorDayProfile Profile()
    {
        var kiosks = new Dictionary<int, KioskFact>
        {
            [1] = Stand(1, "Meridian Systems"),
            [2] = Stand(2, "Nordwind Machinery"),
            [3] = Stand(3, "Missed Opportunity Ltd"),
        };

        var visits = new List<VisitFact>
        {
            new(1, 1, 1, 1, 1, 11, 420, DwellLevel.Strong, DateTime.UtcNow),
            new(1, 2, 2, 1, 1, 11, 90, DwellLevel.Interested, DateTime.UtcNow),
        };

        return new InterestAnalyser().Build(
            1, Day, visits, kiosks,
            new Dictionary<int, string> { [1] = "Textile Machinery", [11] = "Weaving" },
            new HashSet<int> { 1 });
    }

    [Fact]
    public void TheReportSaysWhereTheyWentWhatTheyMissedAndHowItWasMeasured()
    {
        var built = new DailyReportBuilder().Build(
            new ReportRecipient(1, "Sara Khan", "sara@example.com", "Meridian"),
            Profile(),
            new ExhibitionSettings { Name = "SMA Tech Expo", OrganiserName = "SMA Technology" },
            new DwellSettings(),
            new PackLink("https://example.com/d/TOKEN", DateTime.UtcNow.AddDays(7), 2, 4_200_000));

        // The subject carries the event and what is inside, not the recipient's
        // own name — that reads as a mail merge, and inbox previews already show
        // who it is addressed to.
        Assert.Contains("SMA Tech Expo", built.Subject);
        Assert.Contains("missed", built.Subject);
        Assert.Contains("Dear Sara", built.Html);

        // What they saw.
        Assert.Contains("Meridian Systems", built.Html);
        Assert.Contains("7 min", built.Html);
        Assert.Contains("Strong interest", built.Html);

        // What they missed, with the stand details that make it actionable.
        Assert.Contains("Missed Opportunity Ltd", built.Html);
        Assert.Contains("H1-003", built.Html);
        Assert.Contains("Zone D7", built.Html);

        // The pack.
        Assert.Contains("https://example.com/d/TOKEN", built.Html);
        Assert.Contains("4 MB", built.Html);

        // And an honest account of how the numbers were produced.
        Assert.Contains("How this was measured", built.Html);
        Assert.Contains("45 seconds", built.Html);
        Assert.Contains("consented", built.Html);

        // The plain-text alternative carries the same substance.
        Assert.Contains("Meridian Systems", built.TextBody);
        Assert.Contains("Missed Opportunity Ltd", built.TextBody);
    }

    [Fact]
    public void AVisitorWithNoRecordedVisitsGetsAnHonestReportRatherThanAnEmptyOne()
    {
        var empty = new InterestAnalyser().Build(
            1, Day, [], new Dictionary<int, KioskFact>(), new Dictionary<int, string>(), new HashSet<int>());

        var built = new DailyReportBuilder().Build(
            new ReportRecipient(1, "Nobody Home", "n@example.com", null),
            empty, new ExhibitionSettings(), new DwellSettings(), null);

        Assert.Contains("did not record any stand visits", built.Html);
        Assert.DoesNotContain("missed", built.Subject);
    }

    [Fact]
    public void ExhibitorSuppliedTextCannotInjectMarkupIntoTheEmail()
    {
        var kiosks = new Dictionary<int, KioskFact>
        {
            [1] = Stand(1, "<img src=x onerror=alert(1)>"),
            [2] = Stand(2, "Legit Co"),
        };

        var visits = new List<VisitFact> { new(1, 1, 1, 1, 1, 11, 300, DwellLevel.Strong, DateTime.UtcNow) };
        var profile = new InterestAnalyser().Build(
            1, Day, visits, kiosks, new Dictionary<int, string> { [1] = "Textile Machinery" }, new HashSet<int>());

        var built = new DailyReportBuilder().Build(
            new ReportRecipient(1, "Sara", "s@example.com", null),
            profile, new ExhibitionSettings(), new DwellSettings(), null);

        // The angle brackets are what make it markup; escaped, the rest of the
        // string is just inert text and may legitimately still appear.
        Assert.DoesNotContain("<img src=x", built.Html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", built.Html);
    }

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:text/html,<script>", null)]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("https://example.com/path", "https://example.com/path")]
    public void OnlyLinksWeWouldPutInAnEmailSurvive(string input, string? expected)
        => Assert.Equal(expected, Html.SafeUrl(input));
}

public class TransferProviderTests
{
    [Theory]
    [InlineData("""{"url":"https://x.test/a"}""", "url", "https://x.test/a")]
    [InlineData("""{"data":{"url":"https://x.test/b"}}""", "data.url", "https://x.test/b")]
    [InlineData("""{"files":[{"link":"https://x.test/c"}]}""", "files.0.link", "https://x.test/c")]
    [InlineData("""{"url":"https://x.test/a"}""", "missing.path", null)]
    [InlineData("https://x.test/plain", "url", "https://x.test/plain")]
    public void FindsTheDownloadUrlWhereverTheGatewayPutIt(string json, string path, string? expected)
        => Assert.Equal(expected, GenericHttpTransferProvider.ExtractUrl(json, path));

    [Fact]
    public void ProvidersReportWhetherTheyAreActuallyConfigured()
    {
        var exhibition = new ExhibitionSettings { PublicBaseUrl = "https://expo.example.com" };
        var local = new LocalLinkTransferProvider(exhibition, new DeliverySettings());
        Assert.True(local.IsConfigured);

        var unconfigured = new GenericHttpTransferProvider(new GenericTransferSettings(), new StubHttpClientFactory());
        Assert.False(unconfigured.IsConfigured);
    }

    [Fact]
    public async Task TheLocalProviderServesThePackOnItsDownloadToken()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "pack");

            var provider = new LocalLinkTransferProvider(
                new ExhibitionSettings { PublicBaseUrl = "https://expo.example.com/" },
                new DeliverySettings { LinkExpiryDays = 7 });

            var result = await provider.UploadAsync(
                new TransferRequest(path, "pack", "TOKEN123", "Sara", null));

            Assert.Equal("https://expo.example.com/d/TOKEN123", result.Url);
            Assert.NotNull(result.ExpiresUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
