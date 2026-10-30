using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Financial.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class FinancialRevenueChargedBases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RevenueChargedBases",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevenueChargedBases",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings");
        }
    }
}
