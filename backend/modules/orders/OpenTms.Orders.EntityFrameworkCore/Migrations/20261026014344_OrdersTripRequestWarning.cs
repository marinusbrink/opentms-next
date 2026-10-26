using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersTripRequestWarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TripRequest_WarningAt",
                schema: "orders",
                table: "TransportOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TripRequest_WarningCode",
                schema: "orders",
                table: "TransportOrders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TripRequest_WarningField",
                schema: "orders",
                table: "TransportOrders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TripRequest_WarningAt",
                schema: "orders",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "TripRequest_WarningCode",
                schema: "orders",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "TripRequest_WarningField",
                schema: "orders",
                table: "TransportOrders");
        }
    }
}
