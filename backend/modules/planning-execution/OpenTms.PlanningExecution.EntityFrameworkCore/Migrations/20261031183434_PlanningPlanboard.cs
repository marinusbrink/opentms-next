using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.PlanningExecution.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class PlanningPlanboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FromCombination",
                schema: "planning",
                table: "TripVehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_TripVehicles_TenantId_VehicleId",
                schema: "planning",
                table: "TripVehicles",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_TripDrivers_TenantId_DriverId",
                schema: "planning",
                table: "TripDrivers",
                columns: new[] { "TenantId", "DriverId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TenantId_PlannedArrivalUtc",
                schema: "planning",
                table: "TransportTrips",
                columns: new[] { "TenantId", "PlannedArrivalUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripVehicles_TenantId_VehicleId",
                schema: "planning",
                table: "TripVehicles");

            migrationBuilder.DropIndex(
                name: "IX_TripDrivers_TenantId_DriverId",
                schema: "planning",
                table: "TripDrivers");

            migrationBuilder.DropIndex(
                name: "IX_TransportTrips_TenantId_PlannedArrivalUtc",
                schema: "planning",
                table: "TransportTrips");

            migrationBuilder.DropColumn(
                name: "FromCombination",
                schema: "planning",
                table: "TripVehicles");
        }
    }
}
