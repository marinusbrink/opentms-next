using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersIntakeNameSimilarityThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /* Design 72 build note 25: the channel's name similarity threshold on the intake (expand only; null = the default). */
            migrationBuilder.AddColumn<decimal>(
                name: "NameSimilarityThreshold",
                schema: "orders",
                table: "OrderIntakes",
                type: "numeric(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NameSimilarityThreshold",
                schema: "orders",
                table: "OrderIntakes");
        }
    }
}
