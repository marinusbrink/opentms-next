using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.PlanningExecution.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class PlanningTransportTripTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "planning");

            migrationBuilder.CreateTable(
                name: "TransportTripTypes",
                schema: "planning",
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
                    DefaultExecution = table.Column<int>(type: "integer", nullable: false),
                    SendToFleetManagementSystem = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_TransportTripTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransportTripTypes_TenantId",
                schema: "planning",
                table: "TransportTripTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportTripTypes_TenantId_Code",
                schema: "planning",
                table: "TransportTripTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportTripTypes_TenantId_Default",
                schema: "planning",
                table: "TransportTripTypes",
                column: "TenantId",
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransportTripTypes",
                schema: "planning");
        }
    }
}
