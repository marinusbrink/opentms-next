using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Financial.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class FinancialMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "financial");

            migrationBuilder.CreateTable(
                name: "InvoicingInboundInvoiceTypes",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CostLedgerAccount = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
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
                    table.PrimaryKey("PK_InvoicingInboundInvoiceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoicingOutboundInvoiceTypes",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    NumberingPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    NumberingIncludeYear = table.Column<bool>(type: "boolean", nullable: false),
                    NumberingIncludeMonth = table.Column<bool>(type: "boolean", nullable: false),
                    NumberingSeparator = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    NumberingDigits = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_InvoicingOutboundInvoiceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoicingVatPercentages",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    SalesLedgerAccount = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PurchaseLedgerAccount = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_InvoicingVatPercentages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoicingVatPercentageRates",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    VatPercentageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingVatPercentageRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicingVatPercentageRates_InvoicingVatPercentages_VatPerc~",
                        column: x => x.VatPercentageId,
                        principalSchema: "financial",
                        principalTable: "InvoicingVatPercentages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TariffsAdditionalServices",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Unit = table.Column<int>(type: "integer", nullable: false),
                    VatPercentageId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevenueLedgerAccount = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CostLedgerAccount = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
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
                    table.PrimaryKey("PK_TariffsAdditionalServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffsAdditionalServices_InvoicingVatPercentages_VatPercen~",
                        column: x => x.VatPercentageId,
                        principalSchema: "financial",
                        principalTable: "InvoicingVatPercentages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TariffsDiscountsOrSurcharges",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    ValueKind = table.Column<int>(type: "integer", nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "integer", nullable: false),
                    VatPercentageId = table.Column<Guid>(type: "uuid", nullable: true),
                    LedgerAccount = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
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
                    table.PrimaryKey("PK_TariffsDiscountsOrSurcharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffsDiscountsOrSurcharges_InvoicingVatPercentages_VatPer~",
                        column: x => x.VatPercentageId,
                        principalSchema: "financial",
                        principalTable: "InvoicingVatPercentages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TariffsDiscountOrSurchargeRegions",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscountOrSurchargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeographicalRegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliesAt = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffsDiscountOrSurchargeRegions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffsDiscountOrSurchargeRegions_TariffsDiscountsOrSurchar~",
                        column: x => x.DiscountOrSurchargeId,
                        principalSchema: "financial",
                        principalTable: "TariffsDiscountsOrSurcharges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TariffsDiscountOrSurchargeValues",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscountOrSurchargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffsDiscountOrSurchargeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffsDiscountOrSurchargeValues_TariffsDiscountsOrSurcharg~",
                        column: x => x.DiscountOrSurchargeId,
                        principalSchema: "financial",
                        principalTable: "TariffsDiscountsOrSurcharges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingInboundInvoiceTypes_TenantId",
                schema: "financial",
                table: "InvoicingInboundInvoiceTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingInboundInvoiceTypes_TenantId_Code",
                schema: "financial",
                table: "InvoicingInboundInvoiceTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingOutboundInvoiceTypes_TenantId",
                schema: "financial",
                table: "InvoicingOutboundInvoiceTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingOutboundInvoiceTypes_TenantId_Code",
                schema: "financial",
                table: "InvoicingOutboundInvoiceTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingOutboundInvoiceTypes_TenantId_Kind_Default",
                schema: "financial",
                table: "InvoicingOutboundInvoiceTypes",
                columns: new[] { "TenantId", "Kind" },
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingVatPercentageRates_TenantId",
                schema: "financial",
                table: "InvoicingVatPercentageRates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingVatPercentageRates_VatPercentageId_ValidFrom",
                schema: "financial",
                table: "InvoicingVatPercentageRates",
                columns: new[] { "VatPercentageId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingVatPercentages_TenantId",
                schema: "financial",
                table: "InvoicingVatPercentages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingVatPercentages_TenantId_Code",
                schema: "financial",
                table: "InvoicingVatPercentages",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingVatPercentages_TenantId_Default",
                schema: "financial",
                table: "InvoicingVatPercentages",
                column: "TenantId",
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TariffsAdditionalServices_TenantId",
                schema: "financial",
                table: "TariffsAdditionalServices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsAdditionalServices_TenantId_Code",
                schema: "financial",
                table: "TariffsAdditionalServices",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TariffsAdditionalServices_VatPercentageId",
                schema: "financial",
                table: "TariffsAdditionalServices",
                column: "VatPercentageId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsDiscountOrSurchargeRegions_Parent_Region",
                schema: "financial",
                table: "TariffsDiscountOrSurchargeRegions",
                columns: new[] { "DiscountOrSurchargeId", "GeographicalRegionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TariffsDiscountOrSurchargeRegions_TenantId",
                schema: "financial",
                table: "TariffsDiscountOrSurchargeRegions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsDiscountOrSurchargeValues_Parent_ValidFrom",
                schema: "financial",
                table: "TariffsDiscountOrSurchargeValues",
                columns: new[] { "DiscountOrSurchargeId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TariffsDiscountOrSurchargeValues_TenantId",
                schema: "financial",
                table: "TariffsDiscountOrSurchargeValues",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsDiscountsOrSurcharges_TenantId",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsDiscountsOrSurcharges_TenantId_Code",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TariffsDiscountsOrSurcharges_VatPercentageId",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges",
                column: "VatPercentageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoicingInboundInvoiceTypes",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "InvoicingOutboundInvoiceTypes",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "InvoicingVatPercentageRates",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsAdditionalServices",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsDiscountOrSurchargeRegions",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsDiscountOrSurchargeValues",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsDiscountsOrSurcharges",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "InvoicingVatPercentages",
                schema: "financial");
        }
    }
}
