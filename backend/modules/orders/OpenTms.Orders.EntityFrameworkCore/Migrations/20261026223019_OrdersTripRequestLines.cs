using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersTripRequestLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TripRequest_LineIds",
                schema: "orders",
                table: "TransportOrderTemplates",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TripRequest_LineIds",
                schema: "orders",
                table: "TransportOrders",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TripRequest_LineIds",
                schema: "orders",
                table: "TransportOrderTemplates");

            migrationBuilder.DropColumn(
                name: "TripRequest_LineIds",
                schema: "orders",
                table: "TransportOrders");
        }
    }
}
