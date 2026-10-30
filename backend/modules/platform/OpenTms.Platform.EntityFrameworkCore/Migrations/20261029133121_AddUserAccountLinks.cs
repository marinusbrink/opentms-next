using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Platform.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAccountLinks",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSecurityStamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LinkedTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LinkedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedSecurityStamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccountLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountLinks_LinkedTenantId",
                schema: "platform",
                table: "UserAccountLinks",
                column: "LinkedTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountLinks_UserTenantId_UserId_LinkedTenantId",
                schema: "platform",
                table: "UserAccountLinks",
                columns: new[] { "UserTenantId", "UserId", "LinkedTenantId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAccountLinks",
                schema: "platform");
        }
    }
}
