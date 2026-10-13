using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.MasterData.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataRelationAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                schema: "masterdata",
                table: "Relations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditorNumber",
                schema: "masterdata",
                table: "Relations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                schema: "masterdata",
                table: "Relations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DebtorNumber",
                schema: "masterdata",
                table: "Relations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RelationExternalIdentifiers",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationExternalIdentifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelationExternalIdentifiers_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationTransportCharacteristics",
                schema: "masterdata",
                columns: table => new
                {
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportCharacteristicsId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationTransportCharacteristics", x => new { x.RelationId, x.TransportCharacteristicsId });
                    table.ForeignKey(
                        name: "FK_RelationTransportCharacteristics_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelationTransportCharacteristics_TransportCharacteristics_T~",
                        column: x => x.TransportCharacteristicsId,
                        principalSchema: "masterdata",
                        principalTable: "TransportCharacteristics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Relations_CurrencyId",
                schema: "masterdata",
                table: "Relations",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId_CreditorNumber",
                schema: "masterdata",
                table: "Relations",
                columns: new[] { "TenantId", "CreditorNumber" },
                unique: true,
                filter: "\"CreditorNumber\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId_DebtorNumber",
                schema: "masterdata",
                table: "Relations",
                columns: new[] { "TenantId", "DebtorNumber" },
                unique: true,
                filter: "\"DebtorNumber\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_RelationExternalIdentifiers_RelationId",
                schema: "masterdata",
                table: "RelationExternalIdentifiers",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationExternalIdentifiers_TenantId_Source_Identifier",
                schema: "masterdata",
                table: "RelationExternalIdentifiers",
                columns: new[] { "TenantId", "Source", "Identifier" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_RelationTransportCharacteristics_TransportCharacteristicsId",
                schema: "masterdata",
                table: "RelationTransportCharacteristics",
                column: "TransportCharacteristicsId");

            migrationBuilder.AddForeignKey(
                name: "FK_Relations_Currencies_CurrencyId",
                schema: "masterdata",
                table: "Relations",
                column: "CurrencyId",
                principalSchema: "masterdata",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Relations_Currencies_CurrencyId",
                schema: "masterdata",
                table: "Relations");

            migrationBuilder.DropTable(
                name: "RelationExternalIdentifiers",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "RelationTransportCharacteristics",
                schema: "masterdata");

            migrationBuilder.DropIndex(
                name: "IX_Relations_CurrencyId",
                schema: "masterdata",
                table: "Relations");

            migrationBuilder.DropIndex(
                name: "IX_Relations_TenantId_CreditorNumber",
                schema: "masterdata",
                table: "Relations");

            migrationBuilder.DropIndex(
                name: "IX_Relations_TenantId_DebtorNumber",
                schema: "masterdata",
                table: "Relations");

            migrationBuilder.DropColumn(
                name: "CarrierCode",
                schema: "masterdata",
                table: "Relations");

            migrationBuilder.DropColumn(
                name: "CreditorNumber",
                schema: "masterdata",
                table: "Relations");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                schema: "masterdata",
                table: "Relations");

            migrationBuilder.DropColumn(
                name: "DebtorNumber",
                schema: "masterdata",
                table: "Relations");
        }
    }
}
