using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Orders.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OrdersOrderIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EditedAfterIntake",
                schema: "orders",
                table: "TransportOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceIntakeId",
                schema: "orders",
                table: "TransportOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IntakeAliases",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ScopeRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    NormalizedText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UseCount = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeAliases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderIntakes",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntakeIndex = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SenderAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    ReviewedPayload = table.Column<string>(type: "jsonb", nullable: true),
                    Resolution = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    MappingConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    ProfileTrusted = table.Column<bool>(type: "boolean", nullable: false),
                    AutoConfirm = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultCustomerRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FixedCustomerRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultTransportOrderTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Corrected = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CustomerRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OpenFieldCount = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderIntakes", x => x.Id);
                    table.CheckConstraint("CK_OrderIntakes_IntakeIndex", "\"IntakeIndex\" >= 0");
                    table.CheckConstraint("CK_OrderIntakes_Status", "\"Status\" BETWEEN 1 AND 3");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TenantId_SourceIntakeId",
                schema: "orders",
                table: "TransportOrders",
                columns: new[] { "TenantId", "SourceIntakeId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeAliases_TenantId",
                schema: "orders",
                table: "IntakeAliases",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeAliases_TenantId_Kind_ScopeRelationId_NormalizedText",
                schema: "orders",
                table: "IntakeAliases",
                columns: new[] { "TenantId", "Kind", "ScopeRelationId", "NormalizedText" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntakes_CustomerReference_Trigram",
                schema: "orders",
                table: "OrderIntakes",
                column: "CustomerReference")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntakes_FileName_Trigram",
                schema: "orders",
                table: "OrderIntakes",
                column: "FileName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntakes_SenderAddress_Trigram",
                schema: "orders",
                table: "OrderIntakes",
                column: "SenderAddress")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntakes_Subject_Trigram",
                schema: "orders",
                table: "OrderIntakes",
                column: "Subject")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntakes_TenantId",
                schema: "orders",
                table: "OrderIntakes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntakes_TenantId_SourceDocumentId_IntakeIndex",
                schema: "orders",
                table: "OrderIntakes",
                columns: new[] { "TenantId", "SourceDocumentId", "IntakeIndex" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntakes_TenantId_Status_CreationTime",
                schema: "orders",
                table: "OrderIntakes",
                columns: new[] { "TenantId", "Status", "CreationTime" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntakeAliases",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "OrderIntakes",
                schema: "orders");

            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_TenantId_SourceIntakeId",
                schema: "orders",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "EditedAfterIntake",
                schema: "orders",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "SourceIntakeId",
                schema: "orders",
                table: "TransportOrders");
        }
    }
}
