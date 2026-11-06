using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Reporting.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class ReportingMetricFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "OrderFacts",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OrderWeek = table.Column<DateOnly>(type: "date", nullable: false),
                    OrderMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CustomerRelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportOrderTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CostOfRevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MarginAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Co2Kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    FactUpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderFacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TripFacts",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TripWeek = table.Column<DateOnly>(type: "date", nullable: false),
                    TripMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Execution = table.Column<int>(type: "integer", nullable: false),
                    CarrierRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrimaryVehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportTripTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Co2Kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    LoadFactor = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    PlannedDistanceM = table.Column<int>(type: "integer", nullable: true),
                    ActualDistanceM = table.Column<int>(type: "integer", nullable: true),
                    StopsMeasured = table.Column<int>(type: "integer", nullable: false),
                    StopsOnTime = table.Column<int>(type: "integer", nullable: false),
                    FactUpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripFacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_TenantId_CustomerRelationId_OrderDate",
                schema: "reporting",
                table: "OrderFacts",
                columns: new[] { "TenantId", "CustomerRelationId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_TenantId_OrderDate",
                schema: "reporting",
                table: "OrderFacts",
                columns: new[] { "TenantId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_TenantId_OrganizationUnitId_OrderDate",
                schema: "reporting",
                table: "OrderFacts",
                columns: new[] { "TenantId", "OrganizationUnitId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_TenantId_TransportOrderTypeId_OrderDate",
                schema: "reporting",
                table: "OrderFacts",
                columns: new[] { "TenantId", "TransportOrderTypeId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_TenantId_CarrierRelationId_TripDate",
                schema: "reporting",
                table: "TripFacts",
                columns: new[] { "TenantId", "CarrierRelationId", "TripDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_TenantId_OrganizationUnitId_TripDate",
                schema: "reporting",
                table: "TripFacts",
                columns: new[] { "TenantId", "OrganizationUnitId", "TripDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_TenantId_PrimaryVehicleId_TripDate",
                schema: "reporting",
                table: "TripFacts",
                columns: new[] { "TenantId", "PrimaryVehicleId", "TripDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_TenantId_TransportTripTypeId_TripDate",
                schema: "reporting",
                table: "TripFacts",
                columns: new[] { "TenantId", "TransportTripTypeId", "TripDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_TenantId_TripDate",
                schema: "reporting",
                table: "TripFacts",
                columns: new[] { "TenantId", "TripDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderFacts",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "TripFacts",
                schema: "reporting");
        }
    }
}
