using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Integrations.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationsOrderIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageRecords",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Agent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CacheReadTokens = table.Column<int>(type: "integer", nullable: false),
                    CacheWriteTokens = table.Column<int>(type: "integer", nullable: false),
                    CostMicroUsd = table.Column<long>(type: "bigint", nullable: false),
                    InboundDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiClients",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultTransportOrderTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutoConfirm = table.Column<bool>(type: "boolean", nullable: false),
                    SecretLastFour = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    SecretCreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_ApiClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportProfiles",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SenderKey = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    Target = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Rules = table.Column<string>(type: "jsonb", nullable: false),
                    PreviousRules = table.Column<string>(type: "jsonb", nullable: true),
                    Signature = table.Column<string>(type: "jsonb", nullable: false),
                    ConfirmedCount = table.Column<int>(type: "integer", nullable: false),
                    CorrectedCount = table.Column<int>(type: "integer", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_ImportProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboundMessages",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    From = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawBlobName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailboxConnections",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    FolderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FolderName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RefreshTokenEncrypted = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SyncCursor = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DefaultCustomerRelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultTransportOrderTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutoConfirm = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_MailboxConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboundDocuments",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboundMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlobName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    SenderKey = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImportProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImportProfileVersion = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MappingMethod = table.Column<int>(type: "integer", nullable: false),
                    Mapping = table.Column<string>(type: "jsonb", nullable: true),
                    IntakeCount = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboundDocuments_InboundMessages_InboundMessageId",
                        column: x => x.InboundMessageId,
                        principalSchema: "integrations",
                        principalTable: "InboundMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InboundDocumentSteps",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboundDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    MessageKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Parameters = table.Column<string>(type: "jsonb", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    CostMicroUsd = table.Column<long>(type: "bigint", nullable: true),
                    IntakeIndex = table.Column<int>(type: "integer", nullable: true),
                    IntakeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundDocumentSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboundDocumentSteps_InboundDocuments_InboundDocumentId",
                        column: x => x.InboundDocumentId,
                        principalSchema: "integrations",
                        principalTable: "InboundDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_TenantId_At",
                schema: "integrations",
                table: "AiUsageRecords",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_ClientId",
                schema: "integrations",
                table: "ApiClients",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_TenantId",
                schema: "integrations",
                table: "ApiClients",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportProfiles_TenantId_Format_SenderKey",
                schema: "integrations",
                table: "ImportProfiles",
                columns: new[] { "TenantId", "Format", "SenderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundDocuments_InboundMessageId",
                schema: "integrations",
                table: "InboundDocuments",
                column: "InboundMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundDocuments_TenantId_ImportProfileId",
                schema: "integrations",
                table: "InboundDocuments",
                columns: new[] { "TenantId", "ImportProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundDocuments_TenantId_InboundMessageId",
                schema: "integrations",
                table: "InboundDocuments",
                columns: new[] { "TenantId", "InboundMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundDocumentSteps_InboundDocumentId",
                schema: "integrations",
                table: "InboundDocumentSteps",
                column: "InboundDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessages_TenantId_Channel_SourceId_ExternalId",
                schema: "integrations",
                table: "InboundMessages",
                columns: new[] { "TenantId", "Channel", "SourceId", "ExternalId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessages_TenantId_ReceivedAt",
                schema: "integrations",
                table: "InboundMessages",
                columns: new[] { "TenantId", "ReceivedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessages_TenantId_Status",
                schema: "integrations",
                table: "InboundMessages",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MailboxConnections_TenantId_Provider_EmailAddress_FolderId",
                schema: "integrations",
                table: "MailboxConnections",
                columns: new[] { "TenantId", "Provider", "EmailAddress", "FolderId" },
                unique: true,
                filter: "\"EmailAddress\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_MailboxConnections_TenantId_Status",
                schema: "integrations",
                table: "MailboxConnections",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageRecords",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "ApiClients",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "ImportProfiles",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "InboundDocumentSteps",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "MailboxConnections",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "InboundDocuments",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "InboundMessages",
                schema: "integrations");
        }
    }
}
