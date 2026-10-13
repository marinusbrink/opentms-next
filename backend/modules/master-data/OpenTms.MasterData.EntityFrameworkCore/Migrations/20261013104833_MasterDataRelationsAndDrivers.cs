using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.MasterData.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataRelationsAndDrivers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "Relations",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FullName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SearchKey = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Roles = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    BlockReason = table.Column<int>(type: "integer", nullable: false),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Mobile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Website = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ChamberOfCommerceNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    VatNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PreferredCulture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
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
                    table.PrimaryKey("PK_Relations", x => x.Id);
                    table.CheckConstraint("CK_Relations_Roles", "\"Roles\" > 0 AND \"Roles\" <= 31");
                });

            migrationBuilder.CreateTable(
                name: "Drivers",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MiddleName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Initials = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SearchKey = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Mobile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PreferredCulture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    EmployerRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    IsAnonymised = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_Drivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Drivers_Relations_EmployerRelationId",
                        column: x => x.EmployerRelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelationAddresses",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Street = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HouseNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    HouseNumberSuffix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AddressLine2 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PostalCodeKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelationAddresses_AddressTypes_AddressTypeId",
                        column: x => x.AddressTypeId,
                        principalSchema: "masterdata",
                        principalTable: "AddressTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationAddresses_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "masterdata",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationAddresses_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationBankAccounts",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    Bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    BankName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccountHolder = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelationBankAccounts_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationCategoryMemberships",
                schema: "masterdata",
                columns: table => new
                {
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationCategoryMemberships", x => new { x.RelationId, x.RelationCategoryId });
                    table.ForeignKey(
                        name: "FK_RelationCategoryMemberships_RelationCategories_RelationCate~",
                        column: x => x.RelationCategoryId,
                        principalSchema: "masterdata",
                        principalTable: "RelationCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelationCategoryMemberships_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationContactPersons",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MiddleName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Department = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Mobile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationContactPersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelationContactPersons_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationGeographicalRegions",
                schema: "masterdata",
                columns: table => new
                {
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeographicalRegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationGeographicalRegions", x => new { x.RelationId, x.GeographicalRegionId });
                    table.ForeignKey(
                        name: "FK_RelationGeographicalRegions_GeographicalRegions_Geographica~",
                        column: x => x.GeographicalRegionId,
                        principalSchema: "masterdata",
                        principalTable: "GeographicalRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelationGeographicalRegions_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationOrganizationUnits",
                schema: "masterdata",
                columns: table => new
                {
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationOrganizationUnits", x => new { x.RelationId, x.OrganizationUnitId });
                    table.ForeignKey(
                        name: "FK_RelationOrganizationUnits_Relations_RelationId",
                        column: x => x.RelationId,
                        principalSchema: "masterdata",
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DriverQualifications",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverQualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverQualifications_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "masterdata",
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverQualifications_DriverId",
                schema: "masterdata",
                table: "DriverQualifications",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverQualifications_TenantId_ValidUntil",
                schema: "masterdata",
                table: "DriverQualifications",
                columns: new[] { "TenantId", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_EmployerRelationId",
                schema: "masterdata",
                table: "Drivers",
                column: "EmployerRelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_TenantId",
                schema: "masterdata",
                table: "Drivers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_TenantId_Code",
                schema: "masterdata",
                table: "Drivers",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_TenantId_SearchKey",
                schema: "masterdata",
                table: "Drivers",
                columns: new[] { "TenantId", "SearchKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_TenantId_UserId",
                schema: "masterdata",
                table: "Drivers",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_RelationAddresses_AddressTypeId",
                schema: "masterdata",
                table: "RelationAddresses",
                column: "AddressTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationAddresses_CountryId",
                schema: "masterdata",
                table: "RelationAddresses",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationAddresses_RelationId_AddressTypeId",
                schema: "masterdata",
                table: "RelationAddresses",
                columns: new[] { "RelationId", "AddressTypeId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_RelationAddresses_TenantId_PostalCodeKey",
                schema: "masterdata",
                table: "RelationAddresses",
                columns: new[] { "TenantId", "PostalCodeKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RelationBankAccounts_RelationId_Default",
                schema: "masterdata",
                table: "RelationBankAccounts",
                column: "RelationId",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_RelationBankAccounts_RelationId_Iban",
                schema: "masterdata",
                table: "RelationBankAccounts",
                columns: new[] { "RelationId", "Iban" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelationCategoryMemberships_RelationCategoryId",
                schema: "masterdata",
                table: "RelationCategoryMemberships",
                column: "RelationCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationContactPersons_RelationId",
                schema: "masterdata",
                table: "RelationContactPersons",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationContactPersons_TenantId_Email",
                schema: "masterdata",
                table: "RelationContactPersons",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_RelationGeographicalRegions_GeographicalRegionId",
                schema: "masterdata",
                table: "RelationGeographicalRegions",
                column: "GeographicalRegionId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationOrganizationUnits_OrganizationUnitId",
                schema: "masterdata",
                table: "RelationOrganizationUnits",
                column: "OrganizationUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Relations_SearchKey_Trigram",
                schema: "masterdata",
                table: "Relations",
                column: "SearchKey")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId",
                schema: "masterdata",
                table: "Relations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId_ChamberOfCommerceNumber",
                schema: "masterdata",
                table: "Relations",
                columns: new[] { "TenantId", "ChamberOfCommerceNumber" },
                unique: true,
                filter: "\"ChamberOfCommerceNumber\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId_Code",
                schema: "masterdata",
                table: "Relations",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId_OwnCompany",
                schema: "masterdata",
                table: "Relations",
                column: "TenantId",
                unique: true,
                filter: "(\"Roles\" & 16) = 16 AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId_Roles",
                schema: "masterdata",
                table: "Relations",
                columns: new[] { "TenantId", "Roles" });

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId_SearchKey",
                schema: "masterdata",
                table: "Relations",
                columns: new[] { "TenantId", "SearchKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Relations_TenantId_VatNumber",
                schema: "masterdata",
                table: "Relations",
                columns: new[] { "TenantId", "VatNumber" },
                unique: true,
                filter: "\"VatNumber\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverQualifications",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "RelationAddresses",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "RelationBankAccounts",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "RelationCategoryMemberships",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "RelationContactPersons",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "RelationGeographicalRegions",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "RelationOrganizationUnits",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "Drivers",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "Relations",
                schema: "masterdata");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
