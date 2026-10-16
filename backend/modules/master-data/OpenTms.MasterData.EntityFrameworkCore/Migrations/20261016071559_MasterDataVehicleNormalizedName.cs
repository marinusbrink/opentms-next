using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.MasterData.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataVehicleNormalizedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_Name",
                schema: "masterdata",
                table: "Vehicles");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                schema: "masterdata",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            /* Existing rows take the key the entity computes (Name is stored trimmed); no release has shipped vehicles yet. */
            migrationBuilder.Sql("UPDATE masterdata.\"Vehicles\" SET \"NormalizedName\" = upper(\"Name\");");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_Name",
                schema: "masterdata",
                table: "Vehicles",
                columns: new[] { "TenantId", "Name" },
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_NormalizedName",
                schema: "masterdata",
                table: "Vehicles",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_Name",
                schema: "masterdata",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_NormalizedName",
                schema: "masterdata",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                schema: "masterdata",
                table: "Vehicles");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_Name",
                schema: "masterdata",
                table: "Vehicles",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
