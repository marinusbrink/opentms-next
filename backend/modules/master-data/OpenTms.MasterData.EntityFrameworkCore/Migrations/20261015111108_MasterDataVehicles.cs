using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.MasterData.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductRelationCodes",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRelationCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductRelationCodes_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "masterdata",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductRelationCodes_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductRelationCodes_TransportUnits_TransportUnitId",
                        column: x => x.TransportUnitId,
                        principalSchema: "masterdata",
                        principalTable: "TransportUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleCombinations",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleCombinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicencePlate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RegistrationCountryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Vin = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    OwnerRelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeDepotRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LengthCm = table.Column<double>(type: "double precision", nullable: true),
                    WidthCm = table.Column<double>(type: "double precision", nullable: true),
                    HeightCm = table.Column<double>(type: "double precision", nullable: true),
                    GrossVehicleWeightKg = table.Column<double>(type: "double precision", nullable: true),
                    EmptyWeightKg = table.Column<double>(type: "double precision", nullable: true),
                    MaxEpq = table.Column<double>(type: "double precision", nullable: true),
                    MaxLoadingMeters = table.Column<double>(type: "double precision", nullable: true),
                    MaxPayloadKg = table.Column<double>(type: "double precision", nullable: true),
                    FuelType = table.Column<int>(type: "integer", nullable: true),
                    EuroClass = table.Column<int>(type: "integer", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_Countries_RegistrationCountryId",
                        column: x => x.RegistrationCountryId,
                        principalSchema: "masterdata",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vehicles_Relations_HomeDepotRelationId",
                        column: x => x.HomeDepotRelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vehicles_Relations_OwnerRelationId",
                        column: x => x.OwnerRelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vehicles_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalSchema: "masterdata",
                        principalTable: "VehicleTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleCombinationMembers",
                schema: "masterdata",
                columns: table => new
                {
                    VehicleCombinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleCombinationMembers", x => new { x.VehicleCombinationId, x.VehicleId });
                    table.CheckConstraint("CK_VehicleCombinationMembers_Position", "\"Position\" >= 1 AND \"Position\" <= 4");
                    table.ForeignKey(
                        name: "FK_VehicleCombinationMembers_VehicleCombinations_VehicleCombin~",
                        column: x => x.VehicleCombinationId,
                        principalSchema: "masterdata",
                        principalTable: "VehicleCombinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VehicleCombinationMembers_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "masterdata",
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleDefaultDrivers",
                schema: "masterdata",
                columns: table => new
                {
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleDefaultDrivers", x => new { x.VehicleId, x.DriverId });
                    table.ForeignKey(
                        name: "FK_VehicleDefaultDrivers_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "masterdata",
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleDefaultDrivers_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "masterdata",
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehicleExternalIdentifiers",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleExternalIdentifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleExternalIdentifiers_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "masterdata",
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehicleOrganizationUnits",
                schema: "masterdata",
                columns: table => new
                {
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleOrganizationUnits", x => new { x.VehicleId, x.OrganizationUnitId });
                    table.ForeignKey(
                        name: "FK_VehicleOrganizationUnits_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "masterdata",
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehicleTransportCharacteristics",
                schema: "masterdata",
                columns: table => new
                {
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportCharacteristicsId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTransportCharacteristics", x => new { x.VehicleId, x.TransportCharacteristicsId });
                    table.ForeignKey(
                        name: "FK_VehicleTransportCharacteristics_TransportCharacteristics_Tr~",
                        column: x => x.TransportCharacteristicsId,
                        principalSchema: "masterdata",
                        principalTable: "TransportCharacteristics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VehicleTransportCharacteristics_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "masterdata",
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductRelationCodes_NormalizedCode_Trigram",
                schema: "masterdata",
                table: "ProductRelationCodes",
                column: "NormalizedCode")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductRelationCodes_ProductId",
                schema: "masterdata",
                table: "ProductRelationCodes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRelationCodes_RelationId",
                schema: "masterdata",
                table: "ProductRelationCodes",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRelationCodes_TenantId",
                schema: "masterdata",
                table: "ProductRelationCodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRelationCodes_TenantId_NormalizedCode",
                schema: "masterdata",
                table: "ProductRelationCodes",
                columns: new[] { "TenantId", "NormalizedCode" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRelationCodes_TransportUnitId",
                schema: "masterdata",
                table: "ProductRelationCodes",
                column: "TransportUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCombinationMembers_TenantId_VehicleId_Active",
                schema: "masterdata",
                table: "VehicleCombinationMembers",
                columns: new[] { "TenantId", "VehicleId" },
                unique: true,
                filter: "\"IsActive\" = true")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCombinationMembers_VehicleId",
                schema: "masterdata",
                table: "VehicleCombinationMembers",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCombinations_TenantId",
                schema: "masterdata",
                table: "VehicleCombinations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCombinations_TenantId_Name",
                schema: "masterdata",
                table: "VehicleCombinations",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleDefaultDrivers_DriverId",
                schema: "masterdata",
                table: "VehicleDefaultDrivers",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleExternalIdentifiers_TenantId_Source_Identifier",
                schema: "masterdata",
                table: "VehicleExternalIdentifiers",
                columns: new[] { "TenantId", "Source", "Identifier" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleExternalIdentifiers_VehicleId",
                schema: "masterdata",
                table: "VehicleExternalIdentifiers",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleOrganizationUnits_OrganizationUnitId",
                schema: "masterdata",
                table: "VehicleOrganizationUnits",
                column: "OrganizationUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_HomeDepotRelationId",
                schema: "masterdata",
                table: "Vehicles",
                column: "HomeDepotRelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_OwnerRelationId",
                schema: "masterdata",
                table: "Vehicles",
                column: "OwnerRelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_RegistrationCountryId",
                schema: "masterdata",
                table: "Vehicles",
                column: "RegistrationCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId",
                schema: "masterdata",
                table: "Vehicles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_Name",
                schema: "masterdata",
                table: "Vehicles",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_RegistrationCountryId_LicencePlate",
                schema: "masterdata",
                table: "Vehicles",
                columns: new[] { "TenantId", "RegistrationCountryId", "LicencePlate" },
                unique: true,
                filter: "\"LicencePlate\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_Vin",
                schema: "masterdata",
                table: "Vehicles",
                columns: new[] { "TenantId", "Vin" },
                unique: true,
                filter: "\"Vin\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_VehicleTypeId",
                schema: "masterdata",
                table: "Vehicles",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTransportCharacteristics_TransportCharacteristicsId",
                schema: "masterdata",
                table: "VehicleTransportCharacteristics",
                column: "TransportCharacteristicsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductRelationCodes",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "VehicleCombinationMembers",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "VehicleDefaultDrivers",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "VehicleExternalIdentifiers",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "VehicleOrganizationUnits",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "VehicleTransportCharacteristics",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "VehicleCombinations",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "Vehicles",
                schema: "masterdata");
        }
    }
}
