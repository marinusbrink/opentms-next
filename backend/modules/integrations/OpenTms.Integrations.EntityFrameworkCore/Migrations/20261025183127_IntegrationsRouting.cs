using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Integrations.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationsRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "RouteCache",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DistanceM = table.Column<int>(type: "integer", nullable: false),
                    DurationS = table.Column<int>(type: "integer", nullable: false),
                    TollDistanceM = table.Column<int>(type: "integer", nullable: true),
                    TollAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TollCurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Co2Kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Polyline = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RouteCache_ExpiresAt",
                schema: "integrations",
                table: "RouteCache",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RouteCache_TenantId_Key",
                schema: "integrations",
                table: "RouteCache",
                columns: new[] { "TenantId", "Key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RouteCache",
                schema: "integrations");
        }
    }
}
