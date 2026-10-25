using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.PlanningExecution.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class PlanningLegsAndTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "Legs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerRelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    IsViaLeg = table.Column<bool>(type: "boolean", nullable: false),
                    ViaDepotRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    SplitFromLegId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginWaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginRelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginRelationAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OriginCity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OriginCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    OriginLatitude = table.Column<double>(type: "double precision", nullable: true),
                    OriginLongitude = table.Column<double>(type: "double precision", nullable: true),
                    OriginTimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OriginPlannedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OriginWindowFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OriginWindowTill = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OriginReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OriginPlannedDurationMin = table.Column<int>(type: "integer", nullable: true),
                    DestinationStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationRelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationRelationAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DestinationCity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DestinationCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    DestinationLatitude = table.Column<double>(type: "double precision", nullable: true),
                    DestinationLongitude = table.Column<double>(type: "double precision", nullable: true),
                    DestinationTimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DestinationPlannedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DestinationWindowFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DestinationWindowTill = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DestinationReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DestinationPlannedDurationMin = table.Column<int>(type: "integer", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripPosition = table.Column<int>(type: "integer", nullable: true),
                    IsCarriedOutSeparately = table.Column<bool>(type: "boolean", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    DistanceM = table.Column<int>(type: "integer", nullable: true),
                    DurationS = table.Column<int>(type: "integer", nullable: true),
                    TollDistanceM = table.Column<int>(type: "integer", nullable: true),
                    RouteProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RoutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EstimatedCo2Kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Co2Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Co2FactorSet = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AllocatedCostAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AllocatedRevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AllocationVersion = table.Column<long>(type: "bigint", nullable: true),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WeightKg = table.Column<double>(type: "double precision", nullable: true),
                    VolumeM3 = table.Column<double>(type: "double precision", nullable: true),
                    LoadingMeters = table.Column<double>(type: "double precision", nullable: true),
                    Epq = table.Column<double>(type: "double precision", nullable: true),
                    LoadFactor = table.Column<double>(type: "double precision", nullable: true),
                    TransportUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemperatureId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiredTransportCharacteristicIds = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("PK_Legs", x => x.Id);
                    table.CheckConstraint("CK_Legs_Position", "\"Position\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "TransportTrips",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TransportTripTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Execution = table.Column<int>(type: "integer", nullable: false),
                    CarrierRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarrierContactPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartureRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartureAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArrivalRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArrivalAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedDeparture = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlannedArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedDepartureUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PlannedArrivalUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActualDeparture = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StopCompositionMode = table.Column<int>(type: "integer", nullable: false),
                    FirstLastStopRule = table.Column<int>(type: "integer", nullable: false),
                    VehicleCombinationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    DriverInstructions = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedFromOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedDistanceM = table.Column<int>(type: "integer", nullable: true),
                    ActualDistanceM = table.Column<int>(type: "integer", nullable: true),
                    PlannedDurationS = table.Column<int>(type: "integer", nullable: true),
                    LoadFactor = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    LoadingMeters = table.Column<double>(type: "double precision", nullable: true),
                    Co2Kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Warnings = table.Column<string>(type: "jsonb", nullable: false),
                    CostAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SearchKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                    table.PrimaryKey("PK_TransportTrips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportTrips_TransportTripTypes_TransportTripTypeId",
                        column: x => x.TransportTripTypeId,
                        principalSchema: "planning",
                        principalTable: "TransportTripTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TripAttributeDefinitions",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_TripAttributeDefinitions", x => x.Id);
                    table.CheckConstraint("CK_TripAttributeDefinitions_Position", "\"Position\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "ViaDepotRules",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    GeographicalRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepotRelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepotAddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArrivalWaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartureWaypointTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Overnight = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_ViaDepotRules", x => x.Id);
                    table.CheckConstraint("CK_ViaDepotRules_Position", "\"Position\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "TripDrivers",
                schema: "planning",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripDrivers", x => new { x.TripId, x.Position });
                    table.CheckConstraint("CK_TripDrivers_Position", "\"Position\" >= 1 AND \"Position\" <= 3");
                    table.ForeignKey(
                        name: "FK_TripDrivers_TransportTrips_TripId",
                        column: x => x.TripId,
                        principalSchema: "planning",
                        principalTable: "TransportTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripStops",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    WaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    PlannedDurationMin = table.Column<int>(type: "integer", nullable: true),
                    PlannedArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedDeparture = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EstimatedArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EstimatedDeparture = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualDeparture = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedCumulativeDistanceM = table.Column<int>(type: "integer", nullable: true),
                    ActualCumulativeDistanceM = table.Column<int>(type: "integer", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripStops", x => x.Id);
                    table.CheckConstraint("CK_TripStops_Position", "\"Position\" >= 1");
                    table.ForeignKey(
                        name: "FK_TripStops_TransportTrips_TripId",
                        column: x => x.TripId,
                        principalSchema: "planning",
                        principalTable: "TransportTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripVehicles",
                schema: "planning",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripVehicles", x => new { x.TripId, x.Position });
                    table.CheckConstraint("CK_TripVehicles_Position", "\"Position\" >= 1 AND \"Position\" <= 3");
                    table.ForeignKey(
                        name: "FK_TripVehicles_TransportTrips_TripId",
                        column: x => x.TripId,
                        principalSchema: "planning",
                        principalTable: "TransportTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripStopLegs",
                schema: "planning",
                columns: table => new
                {
                    TripStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripStopLegs", x => new { x.TripStopId, x.LegId, x.Role });
                    table.ForeignKey(
                        name: "FK_TripStopLegs_TripStops_TripStopId",
                        column: x => x.TripStopId,
                        principalSchema: "planning",
                        principalTable: "TripStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Legs_TenantId_CustomerRelationId",
                schema: "planning",
                table: "Legs",
                columns: new[] { "TenantId", "CustomerRelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Legs_TenantId_OrderId",
                schema: "planning",
                table: "Legs",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Legs_TenantId_OrderLineId_OriginStopId_DestinationStopId",
                schema: "planning",
                table: "Legs",
                columns: new[] { "TenantId", "OrderLineId", "OriginStopId", "DestinationStopId" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Legs_TenantId_OriginPlannedDate",
                schema: "planning",
                table: "Legs",
                columns: new[] { "TenantId", "OriginPlannedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Legs_TenantId_TripId",
                schema: "planning",
                table: "Legs",
                columns: new[] { "TenantId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_Legs_TenantId_Unplanned",
                schema: "planning",
                table: "Legs",
                columns: new[] { "TenantId", "OriginPlannedDate" },
                filter: "\"TripId\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_Name_Trigram",
                schema: "planning",
                table: "TransportTrips",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_Number_Trigram",
                schema: "planning",
                table: "TransportTrips",
                column: "Number")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_SearchKey_Trigram",
                schema: "planning",
                table: "TransportTrips",
                column: "SearchKey")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TenantId_CarrierRelationId",
                schema: "planning",
                table: "TransportTrips",
                columns: new[] { "TenantId", "CarrierRelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TenantId_CreatedFromOrderId",
                schema: "planning",
                table: "TransportTrips",
                columns: new[] { "TenantId", "CreatedFromOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TenantId_Number",
                schema: "planning",
                table: "TransportTrips",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TenantId_PlannedDepartureUtc",
                schema: "planning",
                table: "TransportTrips",
                columns: new[] { "TenantId", "PlannedDepartureUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TenantId_Status_TripDate",
                schema: "planning",
                table: "TransportTrips",
                columns: new[] { "TenantId", "Status", "TripDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTrips_TransportTripTypeId",
                schema: "planning",
                table: "TransportTrips",
                column: "TransportTripTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TripAttributeDefinitions_TenantId_Key",
                schema: "planning",
                table: "TripAttributeDefinitions",
                columns: new[] { "TenantId", "Key" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TripDrivers_DriverId",
                schema: "planning",
                table: "TripDrivers",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_TripStopLegs_LegId_Role",
                schema: "planning",
                table: "TripStopLegs",
                columns: new[] { "LegId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TenantId_RelationAddressId",
                schema: "planning",
                table: "TripStops",
                columns: new[] { "TenantId", "RelationAddressId" });

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TripId_Position",
                schema: "planning",
                table: "TripStops",
                columns: new[] { "TripId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_TripVehicles_VehicleId",
                schema: "planning",
                table: "TripVehicles",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_ViaDepotRules_TenantId",
                schema: "planning",
                table: "ViaDepotRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ViaDepotRules_TenantId_GeographicalRegionId",
                schema: "planning",
                table: "ViaDepotRules",
                columns: new[] { "TenantId", "GeographicalRegionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ViaDepotRules_TenantId_RelationId",
                schema: "planning",
                table: "ViaDepotRules",
                columns: new[] { "TenantId", "RelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ViaDepotRules_TenantId_Scope_Position",
                schema: "planning",
                table: "ViaDepotRules",
                columns: new[] { "TenantId", "Scope", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Legs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "TripAttributeDefinitions",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "TripDrivers",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "TripStopLegs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "TripVehicles",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "ViaDepotRules",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "TripStops",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "TransportTrips",
                schema: "planning");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
