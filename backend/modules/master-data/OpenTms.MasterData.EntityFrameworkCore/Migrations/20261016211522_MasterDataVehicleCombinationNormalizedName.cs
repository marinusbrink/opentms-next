using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.MasterData.EntityFrameworkCore.Migrations
{
    /* Vehicle combination names are unique per tenant regardless of case (PO 2026-10-16), as vehicle names became in
     * MasterDataVehicleNormalizedName: NormalizedName (the trimmed name in upper case) carries the unique key, the index
     * on Name stays for the default sort, no longer unique. Expand only: the column is added, existing rows are filled
     * from Name, then the unique index is built on filled rows. Existing data: the previous key was case-sensitive, so
     * a database could in principle hold "Night shift" next to "NIGHT SHIFT"; no release has shipped combinations yet
     * (they arrived with MasterDataVehicles in the same unreleased run), so no production database can, and no rolling
     * deploy writes rows without the column. A development database holding such a pair stops at the unique index
     * with a duplicate-key error naming IX_VehicleCombinations_TenantId_NormalizedName (the DbMigrator reports it per
     * tenant); rename one of the two and run the DbMigrator again. */
    public partial class MasterDataVehicleCombinationNormalizedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehicleCombinations_TenantId_Name",
                schema: "masterdata",
                table: "VehicleCombinations");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                schema: "masterdata",
                table: "VehicleCombinations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            /* Existing rows take the key the entity computes (Name is stored trimmed; trim() is a no-op guard). */
            migrationBuilder.Sql("UPDATE masterdata.\"VehicleCombinations\" SET \"NormalizedName\" = upper(trim(\"Name\"));");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCombinations_TenantId_Name",
                schema: "masterdata",
                table: "VehicleCombinations",
                columns: new[] { "TenantId", "Name" },
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCombinations_TenantId_NormalizedName",
                schema: "masterdata",
                table: "VehicleCombinations",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehicleCombinations_TenantId_Name",
                schema: "masterdata",
                table: "VehicleCombinations");

            migrationBuilder.DropIndex(
                name: "IX_VehicleCombinations_TenantId_NormalizedName",
                schema: "masterdata",
                table: "VehicleCombinations");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                schema: "masterdata",
                table: "VehicleCombinations");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCombinations_TenantId_Name",
                schema: "masterdata",
                table: "VehicleCombinations",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
