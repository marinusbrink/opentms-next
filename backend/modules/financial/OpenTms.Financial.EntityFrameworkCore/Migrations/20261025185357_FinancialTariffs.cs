using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Financial.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class FinancialTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AppliesAfterOthers",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "BillableKilometrePercentage",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.AddColumn<int>(
                name: "KilometreBasis",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "RoundTrip",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "InvoicingRelationFinancialSettings",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevenueMultipleMatches = table.Column<int>(type: "integer", nullable: false),
                    CostMultipleMatches = table.Column<int>(type: "integer", nullable: false),
                    VatPercentageId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTermDays = table.Column<int>(type: "integer", nullable: true),
                    ChargeFixedPriceByDefault = table.Column<bool>(type: "boolean", nullable: false),
                    SendFreightDocumentsWithInvoice = table.Column<bool>(type: "boolean", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingRelationFinancialSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicingRelationFinancialSettings_InvoicingVatPercentages_~",
                        column: x => x.VatPercentageId,
                        principalSchema: "financial",
                        principalTable: "InvoicingVatPercentages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TariffsPriceLists",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    Multiplier = table.Column<int>(type: "integer", nullable: false),
                    ColumnQuantity = table.Column<int>(type: "integer", nullable: false),
                    StopMatching = table.Column<int>(type: "integer", nullable: false),
                    UnitConversionFactor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTill = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_TariffsPriceLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TariffsRateLines",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    LegId = table.Column<Guid>(type: "uuid", nullable: true),
                    StopId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    AnchorOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AnchorTripId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    DescriptionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantitySource = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OverridesChildren = table.Column<bool>(type: "boolean", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    IsAdjusted = table.Column<bool>(type: "boolean", nullable: false),
                    AppliesAfterOthers = table.Column<bool>(type: "boolean", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: true),
                    PriceListRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PriceListBracketId = table.Column<Guid>(type: "uuid", nullable: true),
                    ColumnPosition = table.Column<int>(type: "integer", nullable: true),
                    DiscountOrSurchargeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscountOrSurchargeValueId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdditionalServiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    VatPercentageId = table.Column<Guid>(type: "uuid", nullable: true),
                    VatPercentageRateId = table.Column<Guid>(type: "uuid", nullable: true),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CalculationVersion = table.Column<long>(type: "bigint", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffsRateLines", x => x.Id);
                    table.CheckConstraint("CK_TariffsRateLines_OneAnchor", "((\"OrderId\" IS NOT NULL)::int + (\"OrderLineId\" IS NOT NULL)::int + (\"LegId\" IS NOT NULL)::int + (\"StopId\" IS NOT NULL)::int + (\"TripId\" IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "InvoicingRelationDiscountsOrSurcharges",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationFinancialSettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountOrSurchargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTill = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingRelationDiscountsOrSurcharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicingRelationDiscountsOrSurcharges_InvoicingRelationFin~",
                        column: x => x.RelationFinancialSettingsId,
                        principalSchema: "financial",
                        principalTable: "InvoicingRelationFinancialSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoicingRelationDiscountsOrSurcharges_TariffsDiscountsOrSu~",
                        column: x => x.DiscountOrSurchargeId,
                        principalSchema: "financial",
                        principalTable: "TariffsDiscountsOrSurcharges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoicingRelationPriceLists",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationFinancialSettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingRelationPriceLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicingRelationPriceLists_InvoicingRelationFinancialSetti~",
                        column: x => x.RelationFinancialSettingsId,
                        principalSchema: "financial",
                        principalTable: "InvoicingRelationFinancialSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoicingRelationPriceLists_TariffsPriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalSchema: "financial",
                        principalTable: "TariffsPriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TariffsPriceListColumns",
                schema: "financial",
                columns: table => new
                {
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpperBound = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffsPriceListColumns", x => new { x.PriceListId, x.Position });
                    table.ForeignKey(
                        name: "FK_TariffsPriceListColumns_TariffsPriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalSchema: "financial",
                        principalTable: "TariffsPriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TariffsPriceListRules",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    FromRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowQuantity = table.Column<int>(type: "integer", nullable: false),
                    MinAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffsPriceListRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffsPriceListRules_TariffsPriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalSchema: "financial",
                        principalTable: "TariffsPriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TariffsPriceListBrackets",
                schema: "financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PriceListRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    UpperBound = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffsPriceListBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffsPriceListBrackets_TariffsPriceListRules_PriceListRul~",
                        column: x => x.PriceListRuleId,
                        principalSchema: "financial",
                        principalTable: "TariffsPriceListRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TariffsPriceListCells",
                schema: "financial",
                columns: table => new
                {
                    PriceListBracketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ColumnPosition = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffsPriceListCells", x => new { x.PriceListBracketId, x.ColumnPosition });
                    table.ForeignKey(
                        name: "FK_TariffsPriceListCells_TariffsPriceListBrackets_PriceListBra~",
                        column: x => x.PriceListBracketId,
                        principalSchema: "financial",
                        principalTable: "TariffsPriceListBrackets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationDiscountsOrSurcharges_DiscountOrSurchargeId",
                schema: "financial",
                table: "InvoicingRelationDiscountsOrSurcharges",
                column: "DiscountOrSurchargeId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationDiscountsOrSurcharges_Settings_Item",
                schema: "financial",
                table: "InvoicingRelationDiscountsOrSurcharges",
                columns: new[] { "RelationFinancialSettingsId", "DiscountOrSurchargeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationDiscountsOrSurcharges_TenantId",
                schema: "financial",
                table: "InvoicingRelationDiscountsOrSurcharges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationFinancialSettings_TenantId",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationFinancialSettings_TenantId_RelationId",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings",
                columns: new[] { "TenantId", "RelationId" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationFinancialSettings_VatPercentageId",
                schema: "financial",
                table: "InvoicingRelationFinancialSettings",
                column: "VatPercentageId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationPriceLists_PriceListId",
                schema: "financial",
                table: "InvoicingRelationPriceLists",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationPriceLists_Settings_Side_PriceList",
                schema: "financial",
                table: "InvoicingRelationPriceLists",
                columns: new[] { "RelationFinancialSettingsId", "Side", "PriceListId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingRelationPriceLists_TenantId",
                schema: "financial",
                table: "InvoicingRelationPriceLists",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsPriceListBrackets_RuleId_Position",
                schema: "financial",
                table: "TariffsPriceListBrackets",
                columns: new[] { "PriceListRuleId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TariffsPriceListBrackets_TenantId",
                schema: "financial",
                table: "TariffsPriceListBrackets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsPriceListCells_TenantId",
                schema: "financial",
                table: "TariffsPriceListCells",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsPriceListColumns_TenantId",
                schema: "financial",
                table: "TariffsPriceListColumns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsPriceListRules_PriceListId_Position",
                schema: "financial",
                table: "TariffsPriceListRules",
                columns: new[] { "PriceListId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_TariffsPriceListRules_TenantId",
                schema: "financial",
                table: "TariffsPriceListRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsPriceLists_TenantId",
                schema: "financial",
                table: "TariffsPriceLists",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsPriceLists_TenantId_Code",
                schema: "financial",
                table: "TariffsPriceLists",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TariffsRateLines_PriceListId",
                schema: "financial",
                table: "TariffsRateLines",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsRateLines_TenantId",
                schema: "financial",
                table: "TariffsRateLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffsRateLines_TenantId_AnchorOrderId",
                schema: "financial",
                table: "TariffsRateLines",
                columns: new[] { "TenantId", "AnchorOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_TariffsRateLines_TenantId_AnchorTripId",
                schema: "financial",
                table: "TariffsRateLines",
                columns: new[] { "TenantId", "AnchorTripId" });

            migrationBuilder.CreateIndex(
                name: "IX_TariffsRateLines_TenantId_LegId",
                schema: "financial",
                table: "TariffsRateLines",
                columns: new[] { "TenantId", "LegId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoicingRelationDiscountsOrSurcharges",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "InvoicingRelationPriceLists",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsPriceListCells",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsPriceListColumns",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsRateLines",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "InvoicingRelationFinancialSettings",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsPriceListBrackets",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsPriceListRules",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "TariffsPriceLists",
                schema: "financial");

            migrationBuilder.DropColumn(
                name: "AppliesAfterOthers",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges");

            migrationBuilder.DropColumn(
                name: "BillableKilometrePercentage",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges");

            migrationBuilder.DropColumn(
                name: "KilometreBasis",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges");

            migrationBuilder.DropColumn(
                name: "RoundTrip",
                schema: "financial",
                table: "TariffsDiscountsOrSurcharges");
        }
    }
}
