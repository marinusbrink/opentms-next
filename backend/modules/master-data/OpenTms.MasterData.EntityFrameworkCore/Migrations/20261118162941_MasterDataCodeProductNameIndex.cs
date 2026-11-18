using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.MasterData.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataCodeProductNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ProductRelationCodes_TenantId_ProductId_NormalizedCode",
                schema: "masterdata",
                table: "ProductRelationCodes",
                columns: new[] { "TenantId", "ProductId", "NormalizedCode" },
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductRelationCodes_TenantId_ProductId_NormalizedCode",
                schema: "masterdata",
                table: "ProductRelationCodes");
        }
    }
}
