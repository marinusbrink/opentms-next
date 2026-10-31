using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Platform.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class EmailDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailMessageRoutes",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmailMessageId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessageRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailMessages",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    To = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Cc = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Bcc = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReplyTo = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Subject = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: true),
                    TextBody = table.Column<string>(type: "text", nullable: true),
                    Tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    BouncedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    BounceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BounceDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SpamComplaintAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FirstOpenedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastOpenedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FirstClickedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastClickedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageRoutes_ProviderMessageId",
                schema: "platform",
                table: "EmailMessageRoutes",
                column: "ProviderMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_ProviderMessageId",
                schema: "platform",
                table: "EmailMessages",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_TenantId_Status_CreationTime",
                schema: "platform",
                table: "EmailMessages",
                columns: new[] { "TenantId", "Status", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailMessageRoutes",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "EmailMessages",
                schema: "platform");
        }
    }
}
