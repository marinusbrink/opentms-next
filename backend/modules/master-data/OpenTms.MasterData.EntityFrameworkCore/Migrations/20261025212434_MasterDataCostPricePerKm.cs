using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.MasterData.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataCostPricePerKm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostPricePerKm",
                schema: "masterdata",
                table: "VehicleTypes",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostPricePerKm",
                schema: "masterdata",
                table: "Vehicles",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostPricePerKm",
                schema: "masterdata",
                table: "VehicleTypes");

            migrationBuilder.DropColumn(
                name: "CostPricePerKm",
                schema: "masterdata",
                table: "Vehicles");
        }
    }
}
