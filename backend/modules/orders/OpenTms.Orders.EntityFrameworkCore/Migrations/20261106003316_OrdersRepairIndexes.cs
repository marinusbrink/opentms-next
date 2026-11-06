using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersRepairIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_CreationTime",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_LastModificationTime",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "LastModificationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_TenantId_CreationTime",
                schema: "orders",
                table: "TransportOrders");

            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_TenantId_LastModificationTime",
                schema: "orders",
                table: "TransportOrders");
        }
    }
}
