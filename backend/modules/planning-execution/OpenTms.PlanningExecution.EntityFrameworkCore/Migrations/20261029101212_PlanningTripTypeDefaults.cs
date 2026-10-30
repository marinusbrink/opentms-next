using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.PlanningExecution.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class PlanningTripTypeDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /* Design 61 "Trip type defaults", expand only: nullable ids, and database defaults for the required columns
             * (empty id lists, MergeByLocationAndActivity = 1, FirstLastStopRule.None = 0) so existing types and the
             * previous release's inserts stay valid. */
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultArrivalAddressId",
                schema: "planning",
                table: "TransportTripTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultArrivalRelationId",
                schema: "planning",
                table: "TransportTripTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultCarrierContactPersonId",
                schema: "planning",
                table: "TransportTripTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultCarrierRelationId",
                schema: "planning",
                table: "TransportTripTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultDepartureAddressId",
                schema: "planning",
                table: "TransportTripTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultDepartureRelationId",
                schema: "planning",
                table: "TransportTripTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DefaultDepartureTime",
                schema: "planning",
                table: "TransportTripTypes",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultDriverIds",
                schema: "planning",
                table: "TransportTripTypes",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "DefaultFirstLastStopRule",
                schema: "planning",
                table: "TransportTripTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultStopCompositionMode",
                schema: "planning",
                table: "TransportTripTypes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultVehicleCombinationId",
                schema: "planning",
                table: "TransportTripTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultVehicleIds",
                schema: "planning",
                table: "TransportTripTypes",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultArrivalAddressId",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultArrivalRelationId",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultCarrierContactPersonId",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultCarrierRelationId",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultDepartureAddressId",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultDepartureRelationId",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultDepartureTime",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultDriverIds",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultFirstLastStopRule",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultStopCompositionMode",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultVehicleCombinationId",
                schema: "planning",
                table: "TransportTripTypes");

            migrationBuilder.DropColumn(
                name: "DefaultVehicleIds",
                schema: "planning",
                table: "TransportTripTypes");
        }
    }
}
