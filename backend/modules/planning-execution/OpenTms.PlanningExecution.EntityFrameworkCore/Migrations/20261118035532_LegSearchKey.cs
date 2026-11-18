using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.PlanningExecution.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class LegSearchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                schema: "planning",
                table: "Legs",
                type: "text",
                nullable: false,
                computedColumnSql: "lower(\"OriginName\" || ' ' || \"DestinationName\" || ' ' || coalesce(\"OriginCity\", '') || ' ' || coalesce(\"DestinationCity\", '') || ' ' || coalesce(\"OriginReference\", '') || ' ' || coalesce(\"DestinationReference\", '') || ' ' || coalesce(\"Remarks\", ''))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Legs_SearchKey_Trigram",
                schema: "planning",
                table: "Legs",
                column: "SearchKey")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Legs_SearchKey_Trigram",
                schema: "planning",
                table: "Legs");

            migrationBuilder.DropColumn(
                name: "SearchKey",
                schema: "planning",
                table: "Legs");
        }
    }
}
