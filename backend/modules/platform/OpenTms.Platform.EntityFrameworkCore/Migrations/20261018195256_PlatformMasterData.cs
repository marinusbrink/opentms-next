using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Platform.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class PlatformMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTypes",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    IsFreightDocument = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeWithInvoice = table.Column<bool>(type: "boolean", nullable: false),
                    ShowInListIndicator = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    SenderAddress = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReplyTo = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    To = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Cc = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Bcc = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SendCopyToSender = table.Column<bool>(type: "boolean", nullable: false),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NumberSequences",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Scheme = table.Column<int>(type: "integer", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    YearPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    YearSuffix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MonthPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MonthSuffix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MinimumDigits = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSequences", x => x.Id);
                    table.CheckConstraint("CK_NumberSequences_MinimumDigits", "\"MinimumDigits\" >= 1 AND \"MinimumDigits\" <= 12");
                });

            migrationBuilder.CreateTable(
                name: "ReportDefinitions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ProviderTemplateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileNamePattern = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableInCustomerPortal = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTypeTransformations",
                schema: "platform",
                columns: table => new
                {
                    DocumentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Argument = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypeTransformations", x => new { x.DocumentTypeId, x.Kind });
                    table.CheckConstraint("CK_DocumentTypeTransformations_Position", "\"Position\" >= 1 AND \"Position\" <= 4");
                    table.ForeignKey(
                        name: "FK_DocumentTypeTransformations_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalSchema: "platform",
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NumberSequenceCounters",
                schema: "platform",
                columns: table => new
                {
                    NumberSequenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSequenceCounters", x => new { x.NumberSequenceId, x.PeriodYear, x.PeriodMonth });
                    table.ForeignKey(
                        name: "FK_NumberSequenceCounters_NumberSequences_NumberSequenceId",
                        column: x => x.NumberSequenceId,
                        principalSchema: "platform",
                        principalTable: "NumberSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_TenantId_Code",
                schema: "platform",
                table: "DocumentTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_TenantId_NormalizedName",
                schema: "platform",
                table: "EmailTemplates",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_TenantId_Purpose_Default",
                schema: "platform",
                table: "EmailTemplates",
                columns: new[] { "TenantId", "Purpose" },
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_NumberSequences_TenantId_NormalizedName",
                schema: "platform",
                table: "NumberSequences",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ReportDefinitions_TenantId_NormalizedName",
                schema: "platform",
                table: "ReportDefinitions",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ReportDefinitions_TenantId_Purpose_Culture_Default",
                schema: "platform",
                table: "ReportDefinitions",
                columns: new[] { "TenantId", "Purpose", "Culture" },
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTypeTransformations",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "EmailTemplates",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "NumberSequenceCounters",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "ReportDefinitions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "DocumentTypes",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "NumberSequences",
                schema: "platform");
        }
    }
}
