using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.MasterData.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataFirstRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "masterdata");

            migrationBuilder.CreateTable(
                name: "AddressTypes",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("PK_AddressTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsoCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsoNumeric = table.Column<int>(type: "integer", nullable: true),
                    MinorUnits = table.Column<int>(type: "integer", nullable: true),
                    Symbol = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                    table.CheckConstraint("CK_Currencies_MinorUnits", "\"MinorUnits\" IS NULL OR (\"MinorUnits\" >= 0 AND \"MinorUnits\" <= 4)");
                });

            migrationBuilder.CreateTable(
                name: "GeographicalRegions",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_GeographicalRegions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    IsAdr = table.Column<bool>(type: "boolean", nullable: false),
                    AdrUnNumber = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    AdrClass = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AdrPackingGroup = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    AdrDescription = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AdrTechnicalName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AdrClassificationCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AdrHazardLabels = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AdrHazardIdentificationNumber = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    AdrTunnelRestrictionCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AdrTransportCategory = table.Column<int>(type: "integer", nullable: true),
                    AdrIsEnvironmentallyHazardous = table.Column<bool>(type: "boolean", nullable: false),
                    AdrIsLimitedQuantity = table.Column<bool>(type: "boolean", nullable: false),
                    AdrIsWaste = table.Column<bool>(type: "boolean", nullable: false),
                    AdrFlashpointCelsius = table.Column<double>(type: "double precision", nullable: true),
                    AdrSpecialProvisions = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DefaultQuantity = table.Column<double>(type: "double precision", nullable: true),
                    DefaultWeightKg = table.Column<double>(type: "double precision", nullable: true),
                    DefaultColli = table.Column<double>(type: "double precision", nullable: true),
                    DefaultVolumeM3 = table.Column<double>(type: "double precision", nullable: true),
                    DefaultLoadingMeters = table.Column<double>(type: "double precision", nullable: true),
                    DefaultEpq = table.Column<double>(type: "double precision", nullable: true),
                    DefaultLengthCm = table.Column<double>(type: "double precision", nullable: true),
                    DefaultWidthCm = table.Column<double>(type: "double precision", nullable: true),
                    DefaultHeightCm = table.Column<double>(type: "double precision", nullable: true),
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
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RelationCategories",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_RelationCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Temperatures",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MinCelsius = table.Column<double>(type: "double precision", nullable: true),
                    MaxCelsius = table.Column<double>(type: "double precision", nullable: true),
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
                    table.PrimaryKey("PK_Temperatures", x => x.Id);
                    table.CheckConstraint("CK_Temperatures_Range", "\"MinCelsius\" IS NULL OR \"MaxCelsius\" IS NULL OR \"MinCelsius\" <= \"MaxCelsius\"");
                });

            migrationBuilder.CreateTable(
                name: "TransportCharacteristics",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_TransportCharacteristics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransportUnits",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    LengthCm = table.Column<double>(type: "double precision", nullable: true),
                    WidthCm = table.Column<double>(type: "double precision", nullable: true),
                    HeightCm = table.Column<double>(type: "double precision", nullable: true),
                    TareWeightKg = table.Column<double>(type: "double precision", nullable: true),
                    MaxLoadKg = table.Column<double>(type: "double precision", nullable: true),
                    IsStackable = table.Column<bool>(type: "boolean", nullable: false),
                    MaxStackCount = table.Column<int>(type: "integer", nullable: true),
                    LoadingMeters = table.Column<double>(type: "double precision", nullable: true),
                    EpqFactor = table.Column<double>(type: "double precision", nullable: true),
                    IsExchangeable = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_TransportUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleTypes",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    MaxEpq = table.Column<double>(type: "double precision", nullable: true),
                    MaxLoadingMeters = table.Column<double>(type: "double precision", nullable: true),
                    DefaultLengthCm = table.Column<double>(type: "double precision", nullable: true),
                    DefaultWidthCm = table.Column<double>(type: "double precision", nullable: true),
                    DefaultHeightCm = table.Column<double>(type: "double precision", nullable: true),
                    DefaultGrossVehicleWeightKg = table.Column<double>(type: "double precision", nullable: true),
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
                    table.PrimaryKey("PK_VehicleTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsoAlpha2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    IsoAlpha3 = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsoNumeric = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    DefaultCurrencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrimaryTimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_Countries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Countries_Currencies_DefaultCurrencyId",
                        column: x => x.DefaultCurrencyId,
                        principalSchema: "masterdata",
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductIncompatibilities",
                schema: "masterdata",
                columns: table => new
                {
                    LeftId = table.Column<Guid>(type: "uuid", nullable: false),
                    RightId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductIncompatibilities", x => new { x.LeftId, x.RightId });
                    table.CheckConstraint("CK_ProductIncompatibilities_NotSelf", "\"LeftId\" <> \"RightId\"");
                    table.ForeignKey(
                        name: "FK_ProductIncompatibilities_Products_LeftId",
                        column: x => x.LeftId,
                        principalSchema: "masterdata",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductIncompatibilities_Products_RightId",
                        column: x => x.RightId,
                        principalSchema: "masterdata",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TemperatureIncompatibilities",
                schema: "masterdata",
                columns: table => new
                {
                    LeftId = table.Column<Guid>(type: "uuid", nullable: false),
                    RightId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemperatureIncompatibilities", x => new { x.LeftId, x.RightId });
                    table.CheckConstraint("CK_TemperatureIncompatibilities_NotSelf", "\"LeftId\" <> \"RightId\"");
                    table.ForeignKey(
                        name: "FK_TemperatureIncompatibilities_Temperatures_LeftId",
                        column: x => x.LeftId,
                        principalSchema: "masterdata",
                        principalTable: "Temperatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TemperatureIncompatibilities_Temperatures_RightId",
                        column: x => x.RightId,
                        principalSchema: "masterdata",
                        principalTable: "Temperatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportUnitIncompatibilities",
                schema: "masterdata",
                columns: table => new
                {
                    LeftId = table.Column<Guid>(type: "uuid", nullable: false),
                    RightId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportUnitIncompatibilities", x => new { x.LeftId, x.RightId });
                    table.CheckConstraint("CK_TransportUnitIncompatibilities_NotSelf", "\"LeftId\" <> \"RightId\"");
                    table.ForeignKey(
                        name: "FK_TransportUnitIncompatibilities_TransportUnits_LeftId",
                        column: x => x.LeftId,
                        principalSchema: "masterdata",
                        principalTable: "TransportUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransportUnitIncompatibilities_TransportUnits_RightId",
                        column: x => x.RightId,
                        principalSchema: "masterdata",
                        principalTable: "TransportUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransportUnitLoadFactors",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoadingCapacityFactor = table.Column<double>(type: "double precision", nullable: true),
                    MaxCapacity = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportUnitLoadFactors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportUnitLoadFactors_TransportUnits_TransportUnitId",
                        column: x => x.TransportUnitId,
                        principalSchema: "masterdata",
                        principalTable: "TransportUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransportUnitLoadFactors_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalSchema: "masterdata",
                        principalTable: "VehicleTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleTypeTransportCharacteristics",
                schema: "masterdata",
                columns: table => new
                {
                    VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportCharacteristicsId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTypeTransportCharacteristics", x => new { x.VehicleTypeId, x.TransportCharacteristicsId });
                    table.ForeignKey(
                        name: "FK_VehicleTypeTransportCharacteristics_TransportCharacteristic~",
                        column: x => x.TransportCharacteristicsId,
                        principalSchema: "masterdata",
                        principalTable: "TransportCharacteristics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VehicleTypeTransportCharacteristics_VehicleTypes_VehicleTyp~",
                        column: x => x.VehicleTypeId,
                        principalSchema: "masterdata",
                        principalTable: "VehicleTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeographicalRegionPostalCodeRanges",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    GeographicalRegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromNumeric = table.Column<int>(type: "integer", nullable: true),
                    TillNumeric = table.Column<int>(type: "integer", nullable: true),
                    Prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicalRegionPostalCodeRanges", x => x.Id);
                    table.CheckConstraint("CK_GeographicalRegionPostalCodeRanges_Form", "(\"Prefix\" IS NOT NULL AND \"FromNumeric\" IS NULL AND \"TillNumeric\" IS NULL) OR (\"Prefix\" IS NULL AND \"FromNumeric\" IS NOT NULL AND \"TillNumeric\" IS NOT NULL AND \"FromNumeric\" <= \"TillNumeric\")");
                    table.ForeignKey(
                        name: "FK_GeographicalRegionPostalCodeRanges_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "masterdata",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicalRegionPostalCodeRanges_GeographicalRegions_Geog~",
                        column: x => x.GeographicalRegionId,
                        principalSchema: "masterdata",
                        principalTable: "GeographicalRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddressTypes_TenantId",
                schema: "masterdata",
                table: "AddressTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressTypes_TenantId_Kind",
                schema: "masterdata",
                table: "AddressTypes",
                columns: new[] { "TenantId", "Kind" },
                unique: true,
                filter: "\"Kind\" <> 0 AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_DefaultCurrencyId",
                schema: "masterdata",
                table: "Countries",
                column: "DefaultCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_TenantId",
                schema: "masterdata",
                table: "Countries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_TenantId_IsoAlpha2",
                schema: "masterdata",
                table: "Countries",
                columns: new[] { "TenantId", "IsoAlpha2" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_TenantId_IsoAlpha3",
                schema: "masterdata",
                table: "Countries",
                columns: new[] { "TenantId", "IsoAlpha3" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_TenantId",
                schema: "masterdata",
                table: "Currencies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_TenantId_Default",
                schema: "masterdata",
                table: "Currencies",
                column: "TenantId",
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_TenantId_IsoCode",
                schema: "masterdata",
                table: "Currencies",
                columns: new[] { "TenantId", "IsoCode" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_GeographicalRegionPostalCodeRanges_CountryId",
                schema: "masterdata",
                table: "GeographicalRegionPostalCodeRanges",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicalRegionPostalCodeRanges_GeographicalRegionId_Cou~",
                schema: "masterdata",
                table: "GeographicalRegionPostalCodeRanges",
                columns: new[] { "GeographicalRegionId", "CountryId" });

            migrationBuilder.CreateIndex(
                name: "IX_GeographicalRegions_TenantId",
                schema: "masterdata",
                table: "GeographicalRegions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicalRegions_TenantId_Name",
                schema: "masterdata",
                table: "GeographicalRegions",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ProductIncompatibilities_RightId",
                schema: "masterdata",
                table: "ProductIncompatibilities",
                column: "RightId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId",
                schema: "masterdata",
                table: "Products",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Name",
                schema: "masterdata",
                table: "Products",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_RelationCategories_TenantId",
                schema: "masterdata",
                table: "RelationCategories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationCategories_TenantId_Name",
                schema: "masterdata",
                table: "RelationCategories",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TemperatureIncompatibilities_RightId",
                schema: "masterdata",
                table: "TemperatureIncompatibilities",
                column: "RightId");

            migrationBuilder.CreateIndex(
                name: "IX_Temperatures_TenantId",
                schema: "masterdata",
                table: "Temperatures",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Temperatures_TenantId_Name",
                schema: "masterdata",
                table: "Temperatures",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportCharacteristics_TenantId",
                schema: "masterdata",
                table: "TransportCharacteristics",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportCharacteristics_TenantId_Code",
                schema: "masterdata",
                table: "TransportCharacteristics",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_TransportUnitIncompatibilities_RightId",
                schema: "masterdata",
                table: "TransportUnitIncompatibilities",
                column: "RightId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportUnitLoadFactors_TransportUnitId_VehicleTypeId",
                schema: "masterdata",
                table: "TransportUnitLoadFactors",
                columns: new[] { "TransportUnitId", "VehicleTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportUnitLoadFactors_VehicleTypeId",
                schema: "masterdata",
                table: "TransportUnitLoadFactors",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportUnits_TenantId",
                schema: "masterdata",
                table: "TransportUnits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportUnits_TenantId_Name",
                schema: "masterdata",
                table: "TransportUnits",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypes_TenantId",
                schema: "masterdata",
                table: "VehicleTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypes_TenantId_Name",
                schema: "masterdata",
                table: "VehicleTypes",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypeTransportCharacteristics_TransportCharacteristic~",
                schema: "masterdata",
                table: "VehicleTypeTransportCharacteristics",
                column: "TransportCharacteristicsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddressTypes",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "GeographicalRegionPostalCodeRanges",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "ProductIncompatibilities",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "RelationCategories",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "TemperatureIncompatibilities",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "TransportUnitIncompatibilities",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "TransportUnitLoadFactors",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "VehicleTypeTransportCharacteristics",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "Countries",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "GeographicalRegions",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "Temperatures",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "TransportUnits",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "TransportCharacteristics",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "VehicleTypes",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "masterdata");
        }
    }
}
