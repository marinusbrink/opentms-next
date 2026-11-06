using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.PlanningExecution.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class PlanningRepairIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TenantId_CreationTime",
                schema: "planning",
                table: "TransportTrips",
                columns: new[] { "TenantId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TenantId_LastModificationTime",
                schema: "planning",
                table: "TransportTrips",
                columns: new[] { "TenantId", "LastModificationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportTrips_TenantId_CreationTime",
                schema: "planning",
                table: "TransportTrips");

            migrationBuilder.DropIndex(
                name: "IX_TransportTrips_TenantId_LastModificationTime",
                schema: "planning",
                table: "TransportTrips");
        }
    }
}
