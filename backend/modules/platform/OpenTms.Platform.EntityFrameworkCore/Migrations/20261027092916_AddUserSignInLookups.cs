using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Platform.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSignInLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSignInLookups",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmailHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSignInLookups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSignInLookups_EmailHash",
                schema: "platform",
                table: "UserSignInLookups",
                column: "EmailHash");

            migrationBuilder.CreateIndex(
                name: "IX_UserSignInLookups_UserTenantId",
                schema: "platform",
                table: "UserSignInLookups",
                column: "UserTenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSignInLookups",
                schema: "platform");
        }
    }
}
