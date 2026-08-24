using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Exb.Data.Migrations
{
    /// <inheritdoc />
    public partial class MobileAppAndProgramme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MobileSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DeviceName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    AppVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobileSessions_Visitors_VisitorId",
                        column: x => x.VisitorId,
                        principalTable: "Visitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SpeakerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SpeakerTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SpeakerOrganisation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Abstract = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HallId = table.Column<int>(type: "int", nullable: true),
                    RoomName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ExhibitorId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    SubCategoryId = table.Column<int>(type: "int", nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    RequiresBooking = table.Column<bool>(type: "bit", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sessions_Categories_SubCategoryId",
                        column: x => x.SubCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sessions_Exhibitors_ExhibitorId",
                        column: x => x.ExhibitorId,
                        principalTable: "Exhibitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sessions_Halls_HallId",
                        column: x => x.HallId,
                        principalTable: "Halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisitorLoginCodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorId = table.Column<int>(type: "int", nullable: false),
                    EmailSentTo = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    RequestedFromIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OutboxEmailId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitorLoginCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitorLoginCodes_Visitors_VisitorId",
                        column: x => x.VisitorId,
                        principalTable: "Visitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionBookmarks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionBookmarks_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionBookmarks_Visitors_VisitorId",
                        column: x => x.VisitorId,
                        principalTable: "Visitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MobileSessions_TokenHash",
                table: "MobileSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobileSessions_VisitorId",
                table: "MobileSessions",
                column: "VisitorId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionBookmarks_SessionId",
                table: "SessionBookmarks",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionBookmarks_VisitorId_SessionId",
                table: "SessionBookmarks",
                columns: new[] { "VisitorId", "SessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_CategoryId",
                table: "Sessions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Code",
                table: "Sessions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_EventDate_HallId",
                table: "Sessions",
                columns: new[] { "EventDate", "HallId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_EventDate_Kind",
                table: "Sessions",
                columns: new[] { "EventDate", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_EventDate_StartsAt",
                table: "Sessions",
                columns: new[] { "EventDate", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExhibitorId",
                table: "Sessions",
                column: "ExhibitorId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_HallId",
                table: "Sessions",
                column: "HallId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_SubCategoryId",
                table: "Sessions",
                column: "SubCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorLoginCodes_ExpiresUtc",
                table: "VisitorLoginCodes",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorLoginCodes_VisitorId_CreatedUtc",
                table: "VisitorLoginCodes",
                columns: new[] { "VisitorId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobileSessions");

            migrationBuilder.DropTable(
                name: "SessionBookmarks");

            migrationBuilder.DropTable(
                name: "VisitorLoginCodes");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
