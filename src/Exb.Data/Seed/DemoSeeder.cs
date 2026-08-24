using Exb.Data.Entities;
using Exb.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Exb.Data.Seed;

/// <summary>
/// Fills an empty database with a plausible exhibition: halls, a product
/// taxonomy, exhibitors laid out on real stand rows with aisles between them,
/// and registered visitors holding badges.
///
/// This is not decoration. Every part of the product — coverage measurement,
/// dwell attribution, the missed-stand engine, the evening pack — needs a floor
/// plan with hundreds of stands before it means anything, and nobody is going to
/// type that in to evaluate the system. It runs only against an empty database.
/// </summary>
public sealed class DemoSeeder(
    IDbContextFactory<ExhibitionDbContext> factory,
    ILogger<DemoSeeder> logger)
{
    private readonly Random _random = new(20260817);

    private static readonly (string Code, string Name, string Colour, string[] Subs)[] Taxonomy =
    [
        ("TEX", "Textile Machinery", "#2f7ed8",
            ["Spinning", "Weaving", "Knitting", "Dyeing & Finishing"]),
        ("PKG", "Packaging & Labelling", "#f28f43",
            ["Filling machines", "Labelling systems", "Cartoning", "Shrink & wrap"]),
        ("AUT", "Automation & Robotics", "#8bbc21",
            ["Industrial robots", "PLC & control", "Vision systems", "Conveying"]),
        ("RFID", "RFID & Auto-ID", "#910000",
            ["Readers & antennas", "Tags & inlays", "Label printers", "Track & trace software"]),
        ("LOG", "Logistics & Warehousing", "#1aadce",
            ["Racking & storage", "Forklifts", "WMS software", "Sortation"]),
        ("PWR", "Power & Energy", "#492970",
            ["Generators", "Solar", "UPS & backup", "Switchgear"]),
        ("PLA", "Plastics & Moulding", "#c42525",
            ["Injection moulding", "Extrusion", "Recycling", "Raw materials"]),
        ("SFT", "Industrial Software", "#a6c96a",
            ["ERP", "MES", "Quality & compliance", "Analytics"]),
    ];

    private static readonly string[] NamePrefixes =
    [
        "Alpha", "Meridian", "Nordwind", "Crescent", "Ironline", "Vertex", "Bluepeak", "Sunrise",
        "Delta", "Orbit", "Falcon", "Granite", "Helios", "Kestrel", "Lumen", "Monsoon",
        "Northgate", "Pinnacle", "Quantum", "Redstone", "Summit", "Tekno", "United", "Vantage",
        "Westfield", "Zenith", "Cobalt", "Emerald", "Fairmont", "Goldline", "Harbour", "Indus",
    ];

    private static readonly string[] NameSuffixes =
    [
        "Industries", "Systems", "Technologies", "Engineering", "Machinery", "Automation",
        "Solutions", "Manufacturing", "Group", "Works", "Instruments", "Controls",
    ];

    private static readonly string[] Countries =
    [
        "Pakistan", "United Arab Emirates", "Türkiye", "Germany", "Italy", "China", "India",
        "Saudi Arabia", "United Kingdom", "Egypt", "Malaysia", "Spain",
    ];

    private static readonly string[] GivenNames =
    [
        "Adeel", "Sara", "Imran", "Fatima", "Bilal", "Ayesha", "Omar", "Hina", "Yusuf", "Zainab",
        "Marco", "Elena", "Thomas", "Anja", "Ahmet", "Leyla", "Rahul", "Priya", "Chen", "Mei",
        "James", "Sophie", "Karim", "Nadia", "Hassan", "Mariam", "Daniel", "Laura",
    ];

    private static readonly string[] FamilyNames =
    [
        "Khan", "Ahmed", "Malik", "Siddiqui", "Rossi", "Bianchi", "Schmidt", "Weber", "Yilmaz",
        "Demir", "Sharma", "Patel", "Wang", "Li", "Smith", "Brown", "Haddad", "Nasser",
    ];

    public async Task<bool> SeedIfEmptyAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Halls.AnyAsync(ct))
        {
            logger.LogInformation("Database already has halls; demo seeding skipped.");
            return false;
        }

        logger.LogInformation("Empty database: seeding a demonstration exhibition.");

        var halls = SeedHalls(db);
        var (categories, subCategories) = SeedCategories(db);
        await db.SaveChangesAsync(ct);

        var exhibitors = SeedExhibitorsAndStands(db, halls, categories, subCategories);
        await db.SaveChangesAsync(ct);

        SeedVisitors(db, 140);
        SeedEventDays(db);
        int sessions = SeedProgramme(db, halls, categories, subCategories);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seeded {Halls} halls, {Categories} categories, {Exhibitors} exhibitors, "
            + "{Sessions} programme sessions and 140 visitors.",
            halls.Count, categories.Count, exhibitors, sessions);

        return true;
    }

    private static List<Hall> SeedHalls(ExhibitionDbContext db)
    {
        var halls = new List<Hall>
        {
            new() { Code = "H1", Name = "Hall 1 — Machinery", WidthM = 72, DepthM = 48, DisplayOrder = 1 },
            new() { Code = "H2", Name = "Hall 2 — Automation", WidthM = 72, DepthM = 48, DisplayOrder = 2 },
            new() { Code = "H3", Name = "Hall 3 — Technology", WidthM = 60, DepthM = 42, DisplayOrder = 3 },
        };
        db.Halls.AddRange(halls);
        return halls;
    }

    private static (List<Category> Top, List<Category> Subs) SeedCategories(ExhibitionDbContext db)
    {
        var top = new List<Category>();
        var subs = new List<Category>();
        int order = 0;

        foreach (var (code, name, colour, subNames) in Taxonomy)
        {
            var category = new Category { Code = code, Name = name, Colour = colour, DisplayOrder = order++ };
            db.Categories.Add(category);
            top.Add(category);

            int subOrder = 0;
            foreach (string subName in subNames)
            {
                var sub = new Category
                {
                    Code = $"{code}-{subOrder + 1}",
                    Name = subName,
                    Parent = category,
                    Colour = colour,
                    DisplayOrder = subOrder++,
                };
                db.Categories.Add(sub);
                subs.Add(sub);
            }
        }

        return (top, subs);
    }

    /// <summary>
    /// Lay stands out the way a floor plan actually works: rows of stands back
    /// to back, with aisles wide enough to walk between them, and a perimeter
    /// gangway. Stand widths vary, because an exhibition where every stand is
    /// the same size would make the antenna provisioning rule look better than
    /// it is.
    /// </summary>
    private int SeedExhibitorsAndStands(
        ExhibitionDbContext db, List<Hall> halls, List<Category> categories, List<Category> subCategories)
    {
        const double Margin = 3.0;
        const double Aisle = 3.5;
        const double RowDepth = 6.0;
        double[] widthChoices = [3, 3, 6, 6, 6, 9, 12];

        int exhibitorCount = 0;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hall in halls)
        {
            int standNumber = 1;

            for (double y = Margin; y + RowDepth <= hall.DepthM - Margin; y += RowDepth + Aisle)
            {
                double x = Margin;
                while (x < hall.WidthM - Margin - 2.5)
                {
                    double width = widthChoices[_random.Next(widthChoices.Length)];
                    if (x + width > hall.WidthM - Margin) width = hall.WidthM - Margin - x;
                    if (width < 2.5) break;

                    var category = categories[_random.Next(categories.Count)];
                    var subs = subCategories.Where(s => s.Parent == category).ToList();
                    var sub = subs.Count > 0 ? subs[_random.Next(subs.Count)] : null;

                    string company = UniqueCompanyName(usedNames);
                    var exhibitor = new Exhibitor
                    {
                        Code = $"EX{++exhibitorCount:D4}",
                        CompanyName = company,
                        Category = category,
                        SubCategory = sub,
                        ContactName = $"{Pick(GivenNames)} {Pick(FamilyNames)}",
                        Email = $"stand@{Slug(company)}.example",
                        Phone = $"+971 4 {_random.Next(200, 900)} {_random.Next(1000, 9999)}",
                        Website = $"https://www.{Slug(company)}.example",
                        Country = Pick(Countries),
                        Summary = $"{sub?.Name ?? category.Name} for industrial customers, on show at {hall.Name}.",
                        ProfileJson = "{}",
                    };
                    db.Exhibitors.Add(exhibitor);

                    db.Kiosks.Add(new Kiosk
                    {
                        Exhibitor = exhibitor,
                        Hall = hall,
                        StandNumber = $"{hall.Code}-{standNumber:D3}",
                        X = Math.Round(x, 2),
                        Y = Math.Round(y, 2),
                        WidthM = width,
                        DepthM = RowDepth,
                        QrToken = Tokens.New(16),
                    });

                    standNumber++;
                    x += width;
                }
            }
        }

        return exhibitorCount;
    }

    private void SeedVisitors(ExhibitionDbContext db, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            string given = Pick(GivenNames);
            string family = Pick(FamilyNames);
            string company = $"{Pick(NamePrefixes)} {Pick(NameSuffixes)}";

            db.Visitors.Add(new Visitor
            {
                // Badge EPCs are SGTIN-96 shaped, matching what the simulator emits.
                BadgeEpc = BadgeEpc(i),
                RegistrationCode = Tokens.RegistrationCode(),
                AccessToken = Tokens.New(24),
                FullName = $"{given} {family}",
                Email = $"{given.ToLowerInvariant()}.{family.ToLowerInvariant()}{i}@visitor.example",
                Phone = $"+92 3{_random.Next(10, 99)} {_random.Next(1000000, 9999999)}",
                Company = company,
                JobTitle = Pick(["Procurement Manager", "Plant Manager", "Managing Director", "Engineer",
                                 "Production Head", "Consultant", "Technical Buyer", "Operations Manager"]),
                Country = Pick(Countries),
                ConsentEmail = _random.NextDouble() > 0.05,
                ConsentTracking = _random.NextDouble() > 0.08,
                ProfileJson = $$"""
                    {"visitorType":"{{Pick(["buyer", "distributor", "manufacturer", "consultant"])}}","purchasingRole":"{{Pick(["decision", "influence", "research"])}}","budgetWindow":"{{Pick(["now", "6m", "12m", "browsing"])}}"}
                    """,
            });
        }
    }

    private static void SeedEventDays(ExhibitionDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (int i = 0; i < 3; i++)
        {
            db.EventDays.Add(new EventDay
            {
                Date = today.AddDays(i),
                Name = $"Day {i + 1}",
                OpensAt = new TimeOnly(10, 0),
                ClosesAt = new TimeOnly(18, 0),
            });
        }
    }

    /// <summary>
    /// A three-day conference programme running alongside the floor.
    ///
    /// It is built from the same taxonomy as the stands rather than from a
    /// separate list of topics, because the whole point of having the programme
    /// in here is that a visitor interested in RFID can be shown both the stands
    /// and the talks in that category from one search. A programme seeded with
    /// unrelated titles would make that feature look like it worked when it did
    /// not.
    /// </summary>
    private int SeedProgramme(
        ExhibitionDbContext db, List<Hall> halls, List<Category> categories, List<Category> subCategories)
    {
        (string Room, int? HallIndex)[] venues =
        [
            ("Main Theatre", 0),
            ("Seminar Room A", 1),
            ("Seminar Room B", 2),
            ("Conference Suite 1", null),   // off the tracked floor, as conference rooms usually are
            ("Business Matchmaking Lounge", null),
        ];

        (SessionKind Kind, string[] Templates)[] shapes =
        [
            (SessionKind.Lecture, [
                "The state of {0} in 2026",
                "{0}: what buyers are actually specifying",
                "Cutting cost per unit in {0}",
                "Standards and compliance for {0}",
            ]),
            (SessionKind.Panel, [
                "Panel: the next five years of {0}",
                "Panel: sourcing {0} across the region",
            ]),
            (SessionKind.Workshop, [
                "Workshop: commissioning {0} on site",
                "Hands-on {0} for maintenance teams",
            ]),
            (SessionKind.Meeting, [
                "Buyer–supplier meetings: {0}",
                "{0} working group",
            ]),
            (SessionKind.Demo, [
                "Live demonstration: {0}",
            ]),
        ];

        string[] languages = ["en", "en", "en", "ar", "ur"];

        var days = Enumerable.Range(0, 3)
            .Select(i => DateOnly.FromDateTime(DateTime.Today).AddDays(i))
            .ToList();

        int count = 0;

        foreach (var date in days)
        {
            // Slots on the half hour from 10:30, which is how a real programme
            // is laid out: nothing scheduled against the opening rush.
            var slot = new TimeOnly(10, 30);

            while (slot < new TimeOnly(17, 0))
            {
                int concurrent = _random.Next(2, 4);

                for (int track = 0; track < concurrent; track++)
                {
                    var (kind, templates) = shapes[_random.Next(shapes.Length)];
                    var category = categories[_random.Next(categories.Count)];
                    var subs = subCategories.Where(s => s.Parent == category).ToList();
                    var sub = subs.Count > 0 && _random.NextDouble() > 0.4
                        ? subs[_random.Next(subs.Count)]
                        : null;

                    var (room, hallIndex) = venues[(track + count) % venues.Length];
                    string topic = (sub ?? category).Name.ToLowerInvariant();

                    int minutes = kind switch
                    {
                        SessionKind.Workshop => 90,
                        SessionKind.Meeting => 60,
                        SessionKind.Demo => 30,
                        _ => 45,
                    };

                    bool ceremonial = false;
                    string title = string.Format(templates[_random.Next(templates.Length)], topic);

                    // Each day opens with something everyone is invited to.
                    if (slot == new TimeOnly(10, 30) && track == 0)
                    {
                        kind = SessionKind.Ceremony;
                        title = date == days[0] ? "Opening ceremony" : $"Day {days.IndexOf(date) + 1} welcome";
                        minutes = 30;
                        ceremonial = true;
                    }

                    db.Sessions.Add(new ProgrammeSession
                    {
                        Code = $"S{++count:D4}",
                        Title = title,
                        Kind = kind,
                        SpeakerName = ceremonial ? null : $"{Pick(GivenNames)} {Pick(FamilyNames)}",
                        SpeakerTitle = ceremonial
                            ? null
                            : Pick(["Head of Engineering", "Technical Director", "Chief Executive",
                                    "Product Manager", "Professor", "Lead Consultant"]),
                        SpeakerOrganisation = ceremonial ? null : $"{Pick(NamePrefixes)} {Pick(NameSuffixes)}",
                        Abstract = ceremonial
                            ? "Opening remarks from the organiser, followed by the ribbon cutting."
                            : $"A {minutes}-minute {kind.ToString().ToLowerInvariant()} on {topic}, "
                              + "covering current practice, what to specify, and where the common mistakes are.",
                        HallId = hallIndex is { } hi ? halls[hi].Id : null,
                        RoomName = room,
                        Category = ceremonial ? null : category,
                        SubCategory = ceremonial ? null : sub,
                        EventDate = date,
                        StartsAt = slot,
                        EndsAt = slot.AddMinutes(minutes),
                        Capacity = kind switch
                        {
                            SessionKind.Workshop => 30,
                            SessionKind.Meeting => 12,
                            SessionKind.Ceremony => 0,
                            _ => 120,
                        },
                        RequiresBooking = kind is SessionKind.Workshop or SessionKind.Meeting,
                        Language = ceremonial ? "en" : Pick(languages),
                    });
                }

                slot = slot.AddMinutes(60);
            }
        }

        return count;
    }

    /// <summary>Matches the simulator's synthetic EPC shape, so seeded badges appear on the floor.</summary>
    private static string BadgeEpc(int serial)
    {
        unchecked
        {
            uint body = (uint)(serial * 2654435761);
            uint tail = (uint)(serial * 40503) & 0xFFFFFF;
            return ("3034257BF4" + body.ToString("X8") + tail.ToString("X6"))[..24];
        }
    }

    private string UniqueCompanyName(HashSet<string> used)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            string name = $"{Pick(NamePrefixes)} {Pick(NameSuffixes)}";
            if (used.Add(name)) return name;
        }

        string fallback = $"{Pick(NamePrefixes)} {Pick(NameSuffixes)} {used.Count}";
        used.Add(fallback);
        return fallback;
    }

    private string Pick(IReadOnlyList<string> options) => options[_random.Next(options.Count)];

    private static string Slug(string value)
        => new(value.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)).ToArray());
}
