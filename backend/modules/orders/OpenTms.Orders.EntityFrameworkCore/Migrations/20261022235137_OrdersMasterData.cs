using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "TimeSlots",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
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
                    table.PrimaryKey("PK_TimeSlots", x => x.Id);
                    table.CheckConstraint("CK_TimeSlots_NotEmpty", "\"StartTime\" <> \"EndTime\"");
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTypes",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    NumberingPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    NumberingIncludeYear = table.Column<bool>(type: "boolean", nullable: false),
                    NumberingIncludeMonth = table.Column<bool>(type: "boolean", nullable: false),
                    NumberingSeparator = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    NumberingDigits = table.Column<int>(type: "integer", nullable: false),
                    RequiredFields = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_TransportOrderTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WaypointTypes",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    Activity = table.Column<int>(type: "integer", nullable: false),
                    SuccessiveWaypointTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelationCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultDurationMinutes = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_WaypointTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaypointTypes_WaypointTypes_SuccessiveWaypointTypeId",
                        column: x => x.SuccessiveWaypointTypeId,
                        principalSchema: "orders",
                        principalTable: "WaypointTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransportOrderTypeDefaultWaypoints",
                schema: "orders",
                columns: table => new
                {
                    TransportOrderTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    WaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportOrderTypeDefaultWaypoints", x => new { x.TransportOrderTypeId, x.Position });
                    table.ForeignKey(
                        name: "FK_TransportOrderTypeDefaultWaypoints_TransportOrderTypes_Tran~",
                        column: x => x.TransportOrderTypeId,
                        principalSchema: "orders",
                        principalTable: "TransportOrderTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransportOrderTypeDefaultWaypoints_WaypointTypes_WaypointTy~",
                        column: x => x.WaypointTypeId,
                        principalSchema: "orders",
                        principalTable: "WaypointTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WaypointTypeFleetManagementCodes",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    WaypointTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vendor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaypointTypeFleetManagementCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaypointTypeFleetManagementCodes_WaypointTypes_WaypointType~",
                        column: x => x.WaypointTypeId,
                        principalSchema: "orders",
                        principalTable: "WaypointTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeSlots_TenantId",
                schema: "orders",
                table: "TimeSlots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeSlots_TenantId_Code",
                schema: "orders",
                table: "TimeSlots",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTypeDefaultWaypoints_TenantId",
                schema: "orders",
                table: "TransportOrderTypeDefaultWaypoints",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTypeDefaultWaypoints_WaypointTypeId",
                schema: "orders",
                table: "TransportOrderTypeDefaultWaypoints",
                column: "WaypointTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTypes_TenantId",
                schema: "orders",
                table: "TransportOrderTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTypes_TenantId_Code",
                schema: "orders",
                table: "TransportOrderTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrderTypes_TenantId_Default",
                schema: "orders",
                table: "TransportOrderTypes",
                column: "TenantId",
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_WaypointTypeFleetManagementCodes_TenantId",
                schema: "orders",
                table: "WaypointTypeFleetManagementCodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WaypointTypeFleetManagementCodes_WaypointTypeId_Vendor",
                schema: "orders",
                table: "WaypointTypeFleetManagementCodes",
                columns: new[] { "WaypointTypeId", "Vendor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaypointTypes_SuccessiveWaypointTypeId",
                schema: "orders",
                table: "WaypointTypes",
                column: "SuccessiveWaypointTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WaypointTypes_TenantId",
                schema: "orders",
                table: "WaypointTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WaypointTypes_TenantId_Code",
                schema: "orders",
                table: "WaypointTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeSlots",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTypeDefaultWaypoints",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "WaypointTypeFleetManagementCodes",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "TransportOrderTypes",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "WaypointTypes",
                schema: "orders");
        }
    }
}
