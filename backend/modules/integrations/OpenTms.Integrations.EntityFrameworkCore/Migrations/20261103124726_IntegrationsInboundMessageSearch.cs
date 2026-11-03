using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Integrations.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationsInboundMessageSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessages_ExternalId_Trigram",
                schema: "integrations",
                table: "InboundMessages",
                column: "ExternalId")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessages_From_Trigram",
                schema: "integrations",
                table: "InboundMessages",
                column: "From")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessages_Subject_Trigram",
                schema: "integrations",
                table: "InboundMessages",
                column: "Subject")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboundMessages_ExternalId_Trigram",
                schema: "integrations",
                table: "InboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_InboundMessages_From_Trigram",
                schema: "integrations",
                table: "InboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_InboundMessages_Subject_Trigram",
                schema: "integrations",
                table: "InboundMessages");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
