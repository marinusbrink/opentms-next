using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Platform.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivedValueStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DerivedValueStates",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedVersion = table.Column<long>(type: "bigint", nullable: false),
                    CalculatedVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivedValueStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DerivedValueStates_TenantId_TargetType_Status",
                schema: "platform",
                table: "DerivedValueStates",
                columns: new[] { "TenantId", "TargetType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DerivedValueStates_TenantId_TargetType_TargetId",
                schema: "platform",
                table: "DerivedValueStates",
                columns: new[] { "TenantId", "TargetType", "TargetId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DerivedValueStates",
                schema: "platform");
        }
    }
}
