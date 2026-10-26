using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.PlanningExecution.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class PlanningLegSourceKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationSourceKey",
                schema: "planning",
                table: "Legs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginSourceKey",
                schema: "planning",
                table: "Legs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationSourceKey",
                schema: "planning",
                table: "Legs");

            migrationBuilder.DropColumn(
                name: "OriginSourceKey",
                schema: "planning",
                table: "Legs");
        }
    }
}
