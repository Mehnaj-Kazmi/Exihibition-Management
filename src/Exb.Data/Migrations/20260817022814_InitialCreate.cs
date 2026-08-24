using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Exb.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    User = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Colour = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    OpensAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    Closed = table.Column<bool>(type: "bit", nullable: false),
                    ClosedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormSchemas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Entity = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSchemas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Halls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    WidthM = table.Column<double>(type: "float", nullable: false),
                    DepthM = table.Column<double>(type: "float", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Halls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEmails",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ToAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ToName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttachmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEmails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReaderEndpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReaderCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderEndpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ValueJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TagPositions",
                columns: table => new
                {
                    Epc = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HallId = table.Column<int>(type: "int", nullable: true),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    Zone = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    KioskId = table.Column<int>(type: "int", nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    UncertaintyM = table.Column<double>(type: "float", nullable: false),
                    BestRssi = table.Column<double>(type: "float", nullable: false),
                    AntennaCount = table.Column<int>(type: "int", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagPositions", x => x.Epc);
                });

            migrationBuilder.CreateTable(
                name: "Visitors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BadgeEpc = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RegistrationCode = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    JobTitle = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ProfileJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConsentEmail = table.Column<bool>(type: "bit", nullable: false),
                    ConsentTracking = table.Column<bool>(type: "bit", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RegisteredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccessToken = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visitors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Exhibitors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    SubCategoryId = table.Column<int>(type: "int", nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LogoPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ProfileJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exhibitors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exhibitors_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exhibitors_Categories_SubCategoryId",
                        column: x => x.SubCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorId = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Html = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InterestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MissedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StandsVisited = table.Column<int>(type: "int", nullable: false),
                    StandsMissed = table.Column<int>(type: "int", nullable: false),
                    TotalDwellSeconds = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneratedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OutboxEmailId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyReports_Visitors_VisitorId",
                        column: x => x.VisitorId,
                        principalTable: "Visitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorId = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ZipPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ZipSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    DownloadToken = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    TransferProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TransferUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TransferExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OutboxEmailId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryJobs_Visitors_VisitorId",
                        column: x => x.VisitorId,
                        principalTable: "Visitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExhibitorId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UploadedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogueAssets_Exhibitors_ExhibitorId",
                        column: x => x.ExhibitorId,
                        principalTable: "Exhibitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Kiosks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExhibitorId = table.Column<int>(type: "int", nullable: false),
                    HallId = table.Column<int>(type: "int", nullable: false),
                    StandNumber = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    WidthM = table.Column<double>(type: "float", nullable: false),
                    DepthM = table.Column<double>(type: "float", nullable: false),
                    QrToken = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kiosks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kiosks_Exhibitors_ExhibitorId",
                        column: x => x.ExhibitorId,
                        principalTable: "Exhibitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Kiosks_Halls_HallId",
                        column: x => x.HallId,
                        principalTable: "Halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorId = table.Column<int>(type: "int", nullable: false),
                    KioskId = table.Column<int>(type: "int", nullable: false),
                    ExhibitorId = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Included = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogueRequests_Kiosks_KioskId",
                        column: x => x.KioskId,
                        principalTable: "Kiosks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CatalogueRequests_Visitors_VisitorId",
                        column: x => x.VisitorId,
                        principalTable: "Visitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorId = table.Column<int>(type: "int", nullable: false),
                    KioskId = table.Column<int>(type: "int", nullable: false),
                    ExhibitorId = table.Column<int>(type: "int", nullable: false),
                    HallId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    SubCategoryId = table.Column<int>(type: "int", nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DwellSeconds = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    MeanConfidence = table.Column<double>(type: "float", nullable: false),
                    MeanMarginM = table.Column<double>(type: "float", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Visits_Kiosks_KioskId",
                        column: x => x.KioskId,
                        principalTable: "Kiosks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visits_Visitors_VisitorId",
                        column: x => x.VisitorId,
                        principalTable: "Visitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Utc",
                table: "AuditEntries",
                column: "Utc");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueAssets_ExhibitorId",
                table: "CatalogueAssets",
                column: "ExhibitorId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueRequests_EventDate_VisitorId",
                table: "CatalogueRequests",
                columns: new[] { "EventDate", "VisitorId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueRequests_KioskId",
                table: "CatalogueRequests",
                column: "KioskId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueRequests_VisitorId_KioskId_EventDate",
                table: "CatalogueRequests",
                columns: new[] { "VisitorId", "KioskId", "EventDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Code",
                table: "Categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReports_EventDate_Status",
                table: "DailyReports",
                columns: new[] { "EventDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReports_VisitorId_EventDate",
                table: "DailyReports",
                columns: new[] { "VisitorId", "EventDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJobs_DownloadToken",
                table: "DeliveryJobs",
                column: "DownloadToken",
                unique: true,
                filter: "[DownloadToken] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJobs_EventDate_Status",
                table: "DeliveryJobs",
                columns: new[] { "EventDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJobs_VisitorId_EventDate",
                table: "DeliveryJobs",
                columns: new[] { "VisitorId", "EventDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventDays_Date",
                table: "EventDays",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitors_CategoryId",
                table: "Exhibitors",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitors_Code",
                table: "Exhibitors",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitors_CompanyName",
                table: "Exhibitors",
                column: "CompanyName");

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitors_SubCategoryId",
                table: "Exhibitors",
                column: "SubCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSchemas_Entity",
                table: "FormSchemas",
                column: "Entity",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FormSchemas_Entity_Name_Version",
                table: "FormSchemas",
                columns: new[] { "Entity", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Halls_Code",
                table: "Halls",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kiosks_ExhibitorId",
                table: "Kiosks",
                column: "ExhibitorId");

            migrationBuilder.CreateIndex(
                name: "IX_Kiosks_HallId_StandNumber",
                table: "Kiosks",
                columns: new[] { "HallId", "StandNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kiosks_QrToken",
                table: "Kiosks",
                column: "QrToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_Status_CreatedUtc",
                table: "OutboxEmails",
                columns: new[] { "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReaderEndpoints_ReaderCode",
                table: "ReaderEndpoints",
                column: "ReaderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagPositions_LastSeenUtc",
                table: "TagPositions",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_AccessToken",
                table: "Visitors",
                column: "AccessToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_BadgeEpc",
                table: "Visitors",
                column: "BadgeEpc",
                unique: true,
                filter: "[BadgeEpc] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_Email",
                table: "Visitors",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_RegistrationCode",
                table: "Visitors",
                column: "RegistrationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Visits_CategoryId_EventDate",
                table: "Visits",
                columns: new[] { "CategoryId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_EventDate_Level",
                table: "Visits",
                columns: new[] { "EventDate", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_IsOpen",
                table: "Visits",
                column: "IsOpen",
                filter: "[IsOpen] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_KioskId_EventDate",
                table: "Visits",
                columns: new[] { "KioskId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_VisitorId_EventDate",
                table: "Visits",
                columns: new[] { "VisitorId", "EventDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "CatalogueAssets");

            migrationBuilder.DropTable(
                name: "CatalogueRequests");

            migrationBuilder.DropTable(
                name: "DailyReports");

            migrationBuilder.DropTable(
                name: "DeliveryJobs");

            migrationBuilder.DropTable(
                name: "EventDays");

            migrationBuilder.DropTable(
                name: "FormSchemas");

            migrationBuilder.DropTable(
                name: "OutboxEmails");

            migrationBuilder.DropTable(
                name: "ReaderEndpoints");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TagPositions");

            migrationBuilder.DropTable(
                name: "Visits");

            migrationBuilder.DropTable(
                name: "Kiosks");

            migrationBuilder.DropTable(
                name: "Visitors");

            migrationBuilder.DropTable(
                name: "Exhibitors");

            migrationBuilder.DropTable(
                name: "Halls");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
