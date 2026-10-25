using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersTransportOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "ConsignmentNoteDocuments",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrintedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PrintedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlobName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileName = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DataSnapshot = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("PK_ConsignmentNoteDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsignmentNoteFixedDescriptions",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                    table.PrimaryKey("PK_ConsignmentNoteFixedDescriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsignmentNoteProfiles",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                    ShowBorders = table.Column<bool>(type: "boolean", nullable: false),
                    ShowQrCode = table.Column<bool>(type: "boolean", nullable: false),
                    GroupByLeg = table.Column<bool>(type: "boolean", nullable: false),
                    Box1Sender = table.Column<int>(type: "integer", nullable: false),
                    Box2Consignee = table.Column<int>(type: "integer", nullable: false),
                    Box3DeliveryPlace = table.Column<int>(type: "integer", nullable: false),
                    Box4TakingOverPlace = table.Column<int>(type: "integer", nullable: false),
                    Box16Carrier = table.Column<int>(type: "integer", nullable: false),
                    Box17SuccessiveCarrier = table.Column<int>(type: "integer", nullable: false),
                    Box21EstablishedBy = table.Column<int>(type: "integer", nullable: false),
                    Box6Columns = table.Column<string>(type: "jsonb", nullable: false),
                    Box13Items = table.Column<string>(type: "jsonb", nullable: false),
                    Box18Items = table.Column<string>(type: "jsonb", nullable: false),
                    FixedDescriptionIds = table.Column<string>(type: "jsonb", nullable: false),
                    ReportDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_ConsignmentNoteProfiles", x => x.Id);
                    table.CheckConstraint("CK_ConsignmentNoteProfiles_Copies", "\"Copies\" BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "OrderAttributeDefinitions",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Target = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_OrderAttributeDefinitions", x => x.Id);
                    table.CheckConstraint("CK_OrderAttributeDefinitions_Position", "\"Position\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "TransportOrders",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TransportOrderTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerRelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CustomerReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    InternalRemarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    InvoicingRemarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DraftConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    SourceTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    CopyIndex = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    VatPercentageId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTermDays = table.Column<int>(type: "integer", nullable: true),
                    MultipleMatches = table.Column<int>(type: "integer", nullable: false),
                    FixedPriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SendFreightDocumentsWithInvoice = table.Column<bool>(type: "boolean", nullable: false),
                    TripRequest_TransportTripTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_DepartureRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_DepartureAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_ArrivalRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_ArrivalAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_PlannedDeparture = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TripRequest_PlannedArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TripRequest_CarrierRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_CarrierContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_VehicleCombinationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_VehicleIds = table.Column<string>(type: "jsonb", nullable: true),
                    TripRequest_DriverIds = table.Column<string>(type: "jsonb", nullable: true),
                    TripRequest_AgreedPurchaseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TripRequest_AgreedPurchaseCurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    TripRequest_TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CostOfRevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MarginAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Co2Kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    ExecutionProgress = table.Column<int>(type: "integer", nullable: false),
                    LineCount = table.Column<int>(type: "integer", nullable: false),
                    StopCount = table.Column<int>(type: "integer", nullable: false),
                    FirstStopName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FirstStopCity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FirstStopDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastStopName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastStopCity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastStopDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TripNumbers = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_TransportOrders", x => x.Id);
                    table.CheckConstraint("CK_TransportOrders_CopyIndex", "\"CopyIndex\" >= 0");
                    table.CheckConstraint("CK_TransportOrders_Status", "\"Status\" BETWEEN 1 AND 6");
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTemplates",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    TransportOrderTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerRelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CustomerReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    InternalRemarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    InvoicingRemarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    VatPercentageId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTermDays = table.Column<int>(type: "integer", nullable: true),
                    MultipleMatches = table.Column<int>(type: "integer", nullable: false),
                    FixedPriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SendFreightDocumentsWithInvoice = table.Column<bool>(type: "boolean", nullable: false),
                    TripRequest_TransportTripTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_DepartureRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_DepartureAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_ArrivalRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_ArrivalAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_DepartureDayOffset = table.Column<int>(type: "integer", nullable: true),
                    TripRequest_DepartureTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    TripRequest_ArrivalDayOffset = table.Column<int>(type: "integer", nullable: true),
                    TripRequest_ArrivalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    TripRequest_CarrierRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_CarrierContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_VehicleCombinationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripRequest_VehicleIds = table.Column<string>(type: "jsonb", nullable: true),
                    TripRequest_DriverIds = table.Column<string>(type: "jsonb", nullable: true),
                    TripRequest_AgreedPurchaseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TripRequest_AgreedPurchaseCurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Recurrence = table.Column<int>(type: "integer", nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "integer", nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: true),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTill = table.Column<DateOnly>(type: "date", nullable: true),
                    LastMaterialisedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SourceOrderId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_TransportOrderTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsignmentNoteDocumentLines",
                schema: "orders",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsignmentNoteDocumentLines", x => new { x.DocumentId, x.LineId });
                    table.ForeignKey(
                        name: "FK_ConsignmentNoteDocumentLines_Document",
                        column: x => x.DocumentId,
                        principalSchema: "orders",
                        principalTable: "ConsignmentNoteDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderDefaultStops",
                schema: "orders",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_WaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Details_RelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_RelationAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_ContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_TimeSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_WindowFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Details_WindowTill = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Details_Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Details_Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Details_PlannedDurationMin = table.Column<int>(type: "integer", nullable: true),
                    DayOffset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderDefaultStops", x => new { x.OrderId, x.Position });
                    table.ForeignKey(
                        name: "FK_TransportOrderDefaultStops_TransportOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "TransportOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderDiscountsOrSurcharges",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountOrSurchargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderDiscountsOrSurcharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportOrderDiscountsOrSurcharges_Order",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "TransportOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderLines",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Freight_TransportUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Freight_ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_ProductRelationCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_TemperatureId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Freight_Positions = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Freight_Colli = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Freight_WeightKg = table.Column<double>(type: "double precision", nullable: true),
                    Freight_VolumeM3 = table.Column<double>(type: "double precision", nullable: true),
                    Freight_LoadingMeters = table.Column<double>(type: "double precision", nullable: true),
                    Freight_LoadFactor = table.Column<double>(type: "double precision", nullable: true),
                    Freight_Epq = table.Column<double>(type: "double precision", nullable: true),
                    Freight_LengthCm = table.Column<double>(type: "double precision", nullable: true),
                    Freight_WidthCm = table.Column<double>(type: "double precision", nullable: true),
                    Freight_HeightCm = table.Column<double>(type: "double precision", nullable: true),
                    Freight_InvoiceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Freight_Remarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Freight_InternalRemarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Freight_ShipperInstructions = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Freight_SpecialAgreements = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Freight_AttachedDocuments = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Freight_PaymentInstruction = table.Column<int>(type: "integer", nullable: false),
                    Freight_ContainerNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Freight_SizeOrIso = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Freight_PinCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Freight_ShippingCompany = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Freight_ShipperRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_Barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    DistanceM = table.Column<int>(type: "integer", nullable: true),
                    DurationS = table.Column<int>(type: "integer", nullable: true),
                    Co2Kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderLines", x => x.Id);
                    table.CheckConstraint("CK_TransportOrderLines_Position", "\"Position\" >= 1");
                    table.ForeignKey(
                        name: "FK_TransportOrderLines_TransportOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "TransportOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderPriceLists",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderPriceLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportOrderPriceLists_TransportOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "TransportOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderReferences",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportOrderReferences_TransportOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "TransportOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderRemarks",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderRemarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportOrderRemarks_TransportOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "TransportOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTemplateDefaultStops",
                schema: "orders",
                columns: table => new
                {
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_WaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Details_RelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_RelationAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_ContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_TimeSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_WindowFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Details_WindowTill = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Details_Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Details_Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Details_PlannedDurationMin = table.Column<int>(type: "integer", nullable: true),
                    DayOffset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderTemplateDefaultStops", x => new { x.TemplateId, x.Position });
                    table.ForeignKey(
                        name: "FK_TransportOrderTemplateDefaultStops_Template",
                        column: x => x.TemplateId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTemplateDiscountsOrSurcharges",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountOrSurchargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderTemplateDiscountsOrSurcharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportOrderTemplateDiscountsOrSurcharges_Template",
                        column: x => x.TemplateId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTemplateLines",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Freight_TransportUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Freight_ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_ProductRelationCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_TemperatureId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Freight_Positions = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Freight_Colli = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Freight_WeightKg = table.Column<double>(type: "double precision", nullable: true),
                    Freight_VolumeM3 = table.Column<double>(type: "double precision", nullable: true),
                    Freight_LoadingMeters = table.Column<double>(type: "double precision", nullable: true),
                    Freight_LoadFactor = table.Column<double>(type: "double precision", nullable: true),
                    Freight_Epq = table.Column<double>(type: "double precision", nullable: true),
                    Freight_LengthCm = table.Column<double>(type: "double precision", nullable: true),
                    Freight_WidthCm = table.Column<double>(type: "double precision", nullable: true),
                    Freight_HeightCm = table.Column<double>(type: "double precision", nullable: true),
                    Freight_InvoiceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Freight_Remarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Freight_InternalRemarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Freight_ShipperInstructions = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Freight_SpecialAgreements = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    Freight_AttachedDocuments = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Freight_PaymentInstruction = table.Column<int>(type: "integer", nullable: false),
                    Freight_ContainerNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Freight_SizeOrIso = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Freight_PinCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Freight_ShippingCompany = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Freight_ShipperRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Freight_Barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    Measurements = table.Column<string>(type: "jsonb", nullable: false),
                    RequiredTransportCharacteristicIds = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderTemplateLines", x => x.Id);
                    table.CheckConstraint("CK_TransportOrderTemplateLines_Position", "\"Position\" >= 1");
                    table.ForeignKey(
                        name: "FK_TransportOrderTemplateLines_Template",
                        column: x => x.TemplateId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTemplatePriceLists",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderTemplatePriceLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportOrderTemplatePriceLists_Template",
                        column: x => x.TemplateId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTemplateReferences",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderTemplateReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportOrderTemplateReferences_Template",
                        column: x => x.TemplateId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderLineCharacteristics",
                schema: "orders",
                columns: table => new
                {
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportCharacteristicId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderLineCharacteristics", x => new { x.LineId, x.TransportCharacteristicId });
                    table.ForeignKey(
                        name: "FK_TransportOrderLineCharacteristics_Line",
                        column: x => x.LineId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderLineMeasurements",
                schema: "orders",
                columns: table => new
                {
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Value = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderLineMeasurements", x => new { x.LineId, x.Kind, x.Position });
                    table.CheckConstraint("CK_TransportOrderLineMeasurements_Position", "\"Position\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_TransportOrderLineMeasurements_Line",
                        column: x => x.LineId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderStops",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Details_WaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Details_RelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_RelationAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_ContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_TimeSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_WindowFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Details_WindowTill = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Details_Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Details_Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Details_PlannedDurationMin = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderStops", x => x.Id);
                    table.CheckConstraint("CK_TransportOrderStops_Position", "\"Position\" >= 1");
                    table.ForeignKey(
                        name: "FK_TransportOrderStops_Line",
                        column: x => x.LineId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTemplateStops",
                schema: "orders",
                columns: table => new
                {
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_WaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Details_RelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_RelationAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_ContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_TimeSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details_WindowFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Details_WindowTill = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Details_Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Details_Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Details_PlannedDurationMin = table.Column<int>(type: "integer", nullable: true),
                    DayOffset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderTemplateStops", x => new { x.LineId, x.Position });
                    table.ForeignKey(
                        name: "FK_TransportOrderTemplateStops_Line",
                        column: x => x.LineId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderTemplateLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteDocumentLines_LineId",
                schema: "orders",
                table: "ConsignmentNoteDocumentLines",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteDocumentLines_TenantId",
                schema: "orders",
                table: "ConsignmentNoteDocumentLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteDocuments_TenantId",
                schema: "orders",
                table: "ConsignmentNoteDocuments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteDocuments_TenantId_Number",
                schema: "orders",
                table: "ConsignmentNoteDocuments",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteDocuments_TenantId_OrderId",
                schema: "orders",
                table: "ConsignmentNoteDocuments",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteDocuments_TenantId_TripId",
                schema: "orders",
                table: "ConsignmentNoteDocuments",
                columns: new[] { "TenantId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteFixedDescriptions_TenantId",
                schema: "orders",
                table: "ConsignmentNoteFixedDescriptions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteFixedDescriptions_TenantId_NormalizedName",
                schema: "orders",
                table: "ConsignmentNoteFixedDescriptions",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteProfiles_TenantId",
                schema: "orders",
                table: "ConsignmentNoteProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteProfiles_TenantId_Default",
                schema: "orders",
                table: "ConsignmentNoteProfiles",
                column: "TenantId",
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ConsignmentNoteProfiles_TenantId_NormalizedName",
                schema: "orders",
                table: "ConsignmentNoteProfiles",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_OrderAttributeDefinitions_TenantId",
                schema: "orders",
                table: "OrderAttributeDefinitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAttributeDefinitions_TenantId_Target_Key",
                schema: "orders",
                table: "OrderAttributeDefinitions",
                columns: new[] { "TenantId", "Target", "Key" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderDefaultStops_TenantId",
                schema: "orders",
                table: "TransportOrderDefaultStops",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderDiscountsOrSurcharges_Order_Ds",
                schema: "orders",
                table: "TransportOrderDiscountsOrSurcharges",
                columns: new[] { "OrderId", "DiscountOrSurchargeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderDiscountsOrSurcharges_TenantId",
                schema: "orders",
                table: "TransportOrderDiscountsOrSurcharges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderLineCharacteristics_TenantId",
                schema: "orders",
                table: "TransportOrderLineCharacteristics",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderLineMeasurements_TenantId",
                schema: "orders",
                table: "TransportOrderLineMeasurements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderLines_OrderId_Position",
                schema: "orders",
                table: "TransportOrderLines",
                columns: new[] { "OrderId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderLines_TenantId",
                schema: "orders",
                table: "TransportOrderLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderPriceLists_OrderId_PriceListId",
                schema: "orders",
                table: "TransportOrderPriceLists",
                columns: new[] { "OrderId", "PriceListId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderPriceLists_TenantId",
                schema: "orders",
                table: "TransportOrderPriceLists",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderReferences_OrderId",
                schema: "orders",
                table: "TransportOrderReferences",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderReferences_TenantId",
                schema: "orders",
                table: "TransportOrderReferences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderRemarks_OrderId",
                schema: "orders",
                table: "TransportOrderRemarks",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderRemarks_TenantId",
                schema: "orders",
                table: "TransportOrderRemarks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_CustomerName_Trigram",
                schema: "orders",
                table: "TransportOrders",
                column: "CustomerName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_CustomerReference_Trigram",
                schema: "orders",
                table: "TransportOrders",
                column: "CustomerReference")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_FirstStopName_Trigram",
                schema: "orders",
                table: "TransportOrders",
                column: "FirstStopName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_LastStopName_Trigram",
                schema: "orders",
                table: "TransportOrders",
                column: "LastStopName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_Number_Trigram",
                schema: "orders",
                table: "TransportOrders",
                column: "Number")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId",
                schema: "orders",
                table: "TransportOrders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_CustomerRelationId",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "CustomerRelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_Number",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_OrderDate",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_Status_OrderDate",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "Status", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_Template_Date_Copy",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "SourceTemplateId", "OrderDate", "CopyIndex" },
                unique: true,
                filter: "\"SourceTemplateId\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TransportOrderTypeId",
                schema: "orders",
                table: "TransportOrders",
                column: "TransportOrderTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderStops_LineId_Position",
                schema: "orders",
                table: "TransportOrderStops",
                columns: new[] { "LineId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderStops_TenantId",
                schema: "orders",
                table: "TransportOrderStops",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplateDefaultStops_TenantId",
                schema: "orders",
                table: "TransportOrderTemplateDefaultStops",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplateDiscountsOrSurcharges_Template_Ds",
                schema: "orders",
                table: "TransportOrderTemplateDiscountsOrSurcharges",
                columns: new[] { "TemplateId", "DiscountOrSurchargeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplateDiscountsOrSurcharges_TenantId",
                schema: "orders",
                table: "TransportOrderTemplateDiscountsOrSurcharges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplateLines_TemplateId_Position",
                schema: "orders",
                table: "TransportOrderTemplateLines",
                columns: new[] { "TemplateId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplateLines_TenantId",
                schema: "orders",
                table: "TransportOrderTemplateLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplatePriceLists_TemplateId_PriceListId",
                schema: "orders",
                table: "TransportOrderTemplatePriceLists",
                columns: new[] { "TemplateId", "PriceListId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplatePriceLists_TenantId",
                schema: "orders",
                table: "TransportOrderTemplatePriceLists",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplateReferences_TemplateId",
                schema: "orders",
                table: "TransportOrderTemplateReferences",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplateReferences_TenantId",
                schema: "orders",
                table: "TransportOrderTemplateReferences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplates_TenantId",
                schema: "orders",
                table: "TransportOrderTemplates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplates_TenantId_CustomerRelationId",
                schema: "orders",
                table: "TransportOrderTemplates",
                columns: new[] { "TenantId", "CustomerRelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplates_TenantId_IsActive",
                schema: "orders",
                table: "TransportOrderTemplates",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplates_TenantId_NormalizedName",
                schema: "orders",
                table: "TransportOrderTemplates",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTemplateStops_TenantId",
                schema: "orders",
                table: "TransportOrderTemplateStops",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsignmentNoteDocumentLines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "ConsignmentNoteFixedDescriptions",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "ConsignmentNoteProfiles",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "OrderAttributeDefinitions",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderDefaultStops",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderDiscountsOrSurcharges",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderLineCharacteristics",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderLineMeasurements",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderPriceLists",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderReferences",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderRemarks",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderStops",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTemplateDefaultStops",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTemplateDiscountsOrSurcharges",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTemplatePriceLists",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTemplateReferences",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTemplateStops",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "ConsignmentNoteDocuments",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderLines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTemplateLines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrders",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTemplates",
                schema: "orders");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
