using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersCustomerRevenueCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /* Design 74, get_customer_revenue: every column the query reads is in the index, so a vacuumed table answers from the index
             * alone. SQL because EF cannot include CurrencyCode, a column of the owned FinancialTerms. Created before the old index goes,
             * so the previous revision keeps an index for its query during the deploy. */
            migrationBuilder.Sql("""
                CREATE INDEX "IX_TransportOrders_CustomerRevenue" ON orders."TransportOrders" ("TenantId", "CustomerRelationId", "OrderDate")
                INCLUDE ("Status", "CurrencyCode", "RevenueAmount", "IsDeleted");
                """);

            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_TenantId_CustomerRelationId_OrderDate",
                schema: "orders",
                table: "TransportOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_CustomerRelationId_OrderDate",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "CustomerRelationId", "OrderDate" });

            migrationBuilder.Sql("""DROP INDEX orders."IX_TransportOrders_CustomerRevenue";""");
        }
    }
}
