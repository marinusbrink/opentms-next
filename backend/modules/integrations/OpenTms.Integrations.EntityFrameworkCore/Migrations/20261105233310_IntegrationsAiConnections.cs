using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Integrations.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationsAiConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiConnections",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorizationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiConnections_AuthorizationId",
                schema: "integrations",
                table: "AiConnections",
                column: "AuthorizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiConnections_UserId",
                schema: "integrations",
                table: "AiConnections",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiConnections",
                schema: "integrations");
        }
    }
}
