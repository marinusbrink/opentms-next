using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Financial.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class FinancialRelationSettingsName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RelationName",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationFinancialSettings_TenantId_RelationName",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings",
                columns: new[] { "TenantId", "RelationName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoicingRelationFinancialSettings_TenantId_RelationName",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings");

            migrationBuilder.DropColumn(
                name: "RelationName",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings");
        }
    }
}
