using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.PlanningExecution.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class PlanningPlanboardAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RouteProvider",
                schema: "planning",
                table: "TransportTrips",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteSegments",
                schema: "planning",
                table: "TransportTrips",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RoutedAt",
                schema: "planning",
                table: "TransportTrips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TollAmount",
                schema: "planning",
                table: "TransportTrips",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TollCurrencyCode",
                schema: "planning",
                table: "TransportTrips",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TollDistanceM",
                schema: "planning",
                table: "TransportTrips",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResourceUnavailabilities",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceKind = table.Column<int>(type: "integer", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Till = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SpanTillUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RecurrenceWeekdays = table.Column<int>(type: "integer", nullable: true),
                    RecurrenceUntil = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_ResourceUnavailabilities", x => x.Id);
                    table.CheckConstraint("CK_ResourceUnavailabilities_Recurrence", "(\"RecurrenceWeekdays\" IS NULL) = (\"RecurrenceUntil\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "ResourceWorkingPatterns",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceKind = table.Column<int>(type: "integer", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weekday = table.Column<int>(type: "integer", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    End = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceWorkingPatterns", x => x.Id);
                    table.CheckConstraint("CK_ResourceWorkingPatterns_Times", "\"End\" > \"Start\"");
                    table.CheckConstraint("CK_ResourceWorkingPatterns_Weekday", "\"Weekday\" >= 0 AND \"Weekday\" <= 6");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceUnavailabilities_TenantId_FromUtc_SpanTillUtc",
                schema: "planning",
                table: "ResourceUnavailabilities",
                columns: new[] { "TenantId", "FromUtc", "SpanTillUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceUnavailabilities_TenantId_ResourceKind_ResourceId_F~",
                schema: "planning",
                table: "ResourceUnavailabilities",
                columns: new[] { "TenantId", "ResourceKind", "ResourceId", "FromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceWorkingPatterns_TenantId_ResourceKind_ResourceId_We~",
                schema: "planning",
                table: "ResourceWorkingPatterns",
                columns: new[] { "TenantId", "ResourceKind", "ResourceId", "Weekday" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceUnavailabilities",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "ResourceWorkingPatterns",
                schema: "planning");

            migrationBuilder.DropColumn(
                name: "RouteProvider",
                schema: "planning",
                table: "TransportTrips");

            migrationBuilder.DropColumn(
                name: "RouteSegments",
                schema: "planning",
                table: "TransportTrips");

            migrationBuilder.DropColumn(
                name: "RoutedAt",
                schema: "planning",
                table: "TransportTrips");

            migrationBuilder.DropColumn(
                name: "TollAmount",
                schema: "planning",
                table: "TransportTrips");

            migrationBuilder.DropColumn(
                name: "TollCurrencyCode",
                schema: "planning",
                table: "TransportTrips");

            migrationBuilder.DropColumn(
                name: "TollDistanceM",
                schema: "planning",
                table: "TransportTrips");
        }
    }
}
