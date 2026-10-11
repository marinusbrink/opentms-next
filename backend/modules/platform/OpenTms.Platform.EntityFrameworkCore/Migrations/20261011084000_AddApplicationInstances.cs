using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Platform.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationInstances",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MachineName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProcessId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Environment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuntimeVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessorCount = table.Column<int>(type: "integer", nullable: false),
                    OutboxDispatchEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StoppedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    HealthStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    HealthCheckedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    HealthDetails = table.Column<string>(type: "jsonb", nullable: true),
                    WorkingSetBytes = table.Column<long>(type: "bigint", nullable: false),
                    ManagedMemoryBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationInstances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationInstances_LastHeartbeatAt",
                schema: "platform",
                table: "ApplicationInstances",
                column: "LastHeartbeatAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationInstances_Role",
                schema: "platform",
                table: "ApplicationInstances",
                column: "Role");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationInstances",
                schema: "platform");
        }
    }
}
