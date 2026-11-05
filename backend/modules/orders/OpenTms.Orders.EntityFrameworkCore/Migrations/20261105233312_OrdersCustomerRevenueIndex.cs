using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersCustomerRevenueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_CustomerRelationId_OrderDate",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "CustomerRelationId", "OrderDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_TenantId_CustomerRelationId_OrderDate",
                schema: "orders",
                table: "TransportOrders");
        }
    }
}
