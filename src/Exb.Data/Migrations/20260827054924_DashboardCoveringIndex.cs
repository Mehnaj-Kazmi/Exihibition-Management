using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Exb.Data.Migrations
{
    /// <inheritdoc />
    public partial class DashboardCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_EventDate_Level",
                table: "Visits");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_EventDate_Level",
                table: "Visits",
                columns: new[] { "EventDate", "Level" })
                .Annotation("SqlServer:Include", new[] { "KioskId", "VisitorId", "CategoryId", "DwellSeconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_EventDate_Level",
                table: "Visits");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_EventDate_Level",
                table: "Visits",
                columns: new[] { "EventDate", "Level" });
        }
    }
}
