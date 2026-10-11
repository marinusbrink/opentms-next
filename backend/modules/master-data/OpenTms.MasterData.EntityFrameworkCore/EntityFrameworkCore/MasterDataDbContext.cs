using Microsoft.EntityFrameworkCore;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Incompatibilities;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.EntityFrameworkCore.Localization;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.MasterData.EntityFrameworkCore;

[ConnectionStringName(MasterDataDbProperties.ConnectionStringName)]
public class MasterDataDbContext : AbpDbContext<MasterDataDbContext>
{
    public DbSet<Currency> Currencies { get; set; } = default!;
    public DbSet<Country> Countries { get; set; } = default!;
    public DbSet<AddressType> AddressTypes { get; set; } = default!;
    public DbSet<RelationCategory> RelationCategories { get; set; } = default!;
    public DbSet<TransportCharacteristics> TransportCharacteristics { get; set; } = default!;
    public DbSet<VehicleType> VehicleTypes { get; set; } = default!;
    public DbSet<VehicleTypeTransportCharacteristics> VehicleTypeTransportCharacteristics { get; set; } = default!;
    public DbSet<Temperature> Temperatures { get; set; } = default!;
    public DbSet<TemperatureIncompatibility> TemperatureIncompatibilities { get; set; } = default!;
    public DbSet<Product> Products { get; set; } = default!;
    public DbSet<ProductIncompatibility> ProductIncompatibilities { get; set; } = default!;
    public DbSet<TransportUnit> TransportUnits { get; set; } = default!;
    public DbSet<TransportUnitLoadFactor> TransportUnitLoadFactors { get; set; } = default!;
    public DbSet<TransportUnitIncompatibility> TransportUnitIncompatibilities { get; set; } = default!;
    public DbSet<GeographicalRegion> GeographicalRegions { get; set; } = default!;
    public DbSet<GeographicalRegionPostalCodeRange> GeographicalRegionPostalCodeRanges { get; set; } = default!;

    public MasterDataDbContext(DbContextOptions<MasterDataDbContext> options)
        : base(options)
    {
    }

    /* Every unique key is per tenant with NULLS NOT DISTINCT (assessment §4: PostgreSQL treats nulls as
     * distinct, which would allow duplicate host rows later) and ignores soft-deleted rows, so a deleted
     * row frees its key. The filter is PostgreSQL syntax; SQLite (unit tests) accepts the same text. */
    private const string NotDeleted = "\"IsDeleted\" = false";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("masterdata").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(MasterDataDbProperties.DbSchema);

        builder.Entity<Currency>(b =>
        {
            b.ToTable("Currencies", MasterDataDbProperties.DbSchema, t => t.HasCheckConstraint("CK_Currencies_MinorUnits", "\"MinorUnits\" IS NULL OR (\"MinorUnits\" >= 0 AND \"MinorUnits\" <= 4)"));
            b.ConfigureByConvention();
            b.Property(c => c.IsoCode).HasMaxLength(MasterDataConsts.IsoCurrencyCodeLength).IsRequired();
            b.Property(c => c.Symbol).HasMaxLength(MasterDataConsts.MaxSymbolLength);
            b.Property(c => c.Name).HasLocalizedNameColumn();
            b.Property(c => c.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasIndex(c => c.TenantId);
            b.HasIndex(c => new { c.TenantId, c.IsoCode }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* The one enforced default per tenant (assessment §3.8). */
            b.HasIndex(c => c.TenantId, "IX_Currencies_TenantId_Default").IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });

        builder.Entity<Country>(b =>
        {
            b.ToTable("Countries", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(c => c.IsoAlpha2).HasMaxLength(MasterDataConsts.IsoAlpha2Length).IsRequired();
            b.Property(c => c.IsoAlpha3).HasMaxLength(MasterDataConsts.IsoAlpha3Length).IsRequired();
            b.Property(c => c.Name).HasLocalizedNameColumn();
            b.Property(c => c.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.Property(c => c.PrimaryTimeZoneId).HasMaxLength(MasterDataConsts.MaxTimeZoneIdLength);
            b.HasOne<Currency>().WithMany().HasForeignKey(c => c.DefaultCurrencyId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(c => c.TenantId);
            b.HasIndex(c => c.DefaultCurrencyId);
            b.HasIndex(c => new { c.TenantId, c.IsoAlpha2 }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            b.HasIndex(c => new { c.TenantId, c.IsoAlpha3 }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<AddressType>(b =>
        {
            b.ToTable("AddressTypes", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Name).HasLocalizedNameColumn();
            b.Property(t => t.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasIndex(t => t.TenantId);
            /* At most one Visiting and one Postal type per tenant; Other is unbounded. */
            b.HasIndex(t => new { t.TenantId, t.Kind }).IsUnique().AreNullsDistinct(false).HasFilter("\"Kind\" <> 0 AND " + NotDeleted);
        });

        builder.Entity<RelationCategory>(b =>
        {
            b.ToTable("RelationCategories", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(c => c.Name).HasMaxLength(MasterDataConsts.MaxNameLength).IsRequired();
            b.Property(c => c.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasIndex(c => c.TenantId);
            b.HasIndex(c => new { c.TenantId, c.Name }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<TransportCharacteristics>(b =>
        {
            b.ToTable("TransportCharacteristics", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(c => c.Code).HasMaxLength(MasterDataConsts.MaxCodeLength).IsRequired();
            b.Property(c => c.Name).HasMaxLength(MasterDataConsts.MaxNameLength).IsRequired();
            b.Property(c => c.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasIndex(c => c.TenantId);
            b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<VehicleType>(b =>
        {
            b.ToTable("VehicleTypes", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(v => v.Name).HasMaxLength(MasterDataConsts.MaxNameLength).IsRequired();
            b.Property(v => v.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasMany(v => v.Characteristics).WithOne().HasForeignKey(j => j.VehicleTypeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(v => v.TenantId);
            b.HasIndex(v => new { v.TenantId, v.Name }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<VehicleTypeTransportCharacteristics>(b =>
        {
            b.ToTable("VehicleTypeTransportCharacteristics", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(j => new { j.VehicleTypeId, j.TransportCharacteristicsId });
            b.HasOne<TransportCharacteristics>().WithMany().HasForeignKey(j => j.TransportCharacteristicsId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(j => j.TransportCharacteristicsId);
        });

        builder.Entity<Temperature>(b =>
        {
            b.ToTable("Temperatures", MasterDataDbProperties.DbSchema, t => t.HasCheckConstraint("CK_Temperatures_Range", "\"MinCelsius\" IS NULL OR \"MaxCelsius\" IS NULL OR \"MinCelsius\" <= \"MaxCelsius\""));
            b.ConfigureByConvention();
            b.Property(t => t.Name).HasMaxLength(MasterDataConsts.MaxNameLength).IsRequired();
            b.Property(t => t.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        ConfigureIncompatibility<TemperatureIncompatibility, Temperature>(builder, "TemperatureIncompatibilities");

        builder.Entity<Product>(b =>
        {
            b.ToTable("Products", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(p => p.Name).HasMaxLength(MasterDataConsts.MaxNameLength).IsRequired();
            b.Property(p => p.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.Property(p => p.AdrUnNumber).HasMaxLength(MasterDataConsts.AdrUnNumberLength);
            b.Property(p => p.AdrClass).HasMaxLength(MasterDataConsts.MaxAdrClassLength);
            b.Property(p => p.AdrPackingGroup).HasMaxLength(MasterDataConsts.MaxAdrPackingGroupLength);
            b.Property(p => p.AdrDescription).HasMaxLength(MasterDataConsts.MaxAdrDescriptionLength);
            b.Property(p => p.AdrTechnicalName).HasMaxLength(MasterDataConsts.MaxAdrTechnicalNameLength);
            b.Property(p => p.AdrClassificationCode).HasMaxLength(MasterDataConsts.MaxAdrClassificationCodeLength);
            b.Property(p => p.AdrHazardLabels).HasMaxLength(MasterDataConsts.MaxAdrHazardLabelsLength);
            b.Property(p => p.AdrHazardIdentificationNumber).HasMaxLength(MasterDataConsts.AdrHazardIdentificationNumberMaxLength);
            b.Property(p => p.AdrTunnelRestrictionCode).HasMaxLength(MasterDataConsts.MaxAdrTunnelRestrictionCodeLength);
            b.Property(p => p.AdrSpecialProvisions).HasMaxLength(MasterDataConsts.MaxAdrSpecialProvisionsLength);
            b.HasIndex(p => p.TenantId);
            b.HasIndex(p => new { p.TenantId, p.Name }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        ConfigureIncompatibility<ProductIncompatibility, Product>(builder, "ProductIncompatibilities");

        builder.Entity<TransportUnit>(b =>
        {
            b.ToTable("TransportUnits", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(u => u.Name).HasMaxLength(MasterDataConsts.MaxNameLength).IsRequired();
            b.Property(u => u.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasMany(u => u.LoadFactors).WithOne().HasForeignKey(f => f.TransportUnitId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(u => u.TenantId);
            b.HasIndex(u => new { u.TenantId, u.Name }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<TransportUnitLoadFactor>(b =>
        {
            b.ToTable("TransportUnitLoadFactors", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasOne<VehicleType>().WithMany().HasForeignKey(f => f.VehicleTypeId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(f => f.VehicleTypeId);
            b.HasIndex(f => new { f.TransportUnitId, f.VehicleTypeId }).IsUnique();
        });

        ConfigureIncompatibility<TransportUnitIncompatibility, TransportUnit>(builder, "TransportUnitIncompatibilities");

        builder.Entity<GeographicalRegion>(b =>
        {
            b.ToTable("GeographicalRegions", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(r => r.Name).HasMaxLength(MasterDataConsts.MaxNameLength).IsRequired();
            b.Property(r => r.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasMany(r => r.PostalCodeRanges).WithOne().HasForeignKey(p => p.GeographicalRegionId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => new { r.TenantId, r.Name }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<GeographicalRegionPostalCodeRange>(b =>
        {
            b.ToTable("GeographicalRegionPostalCodeRanges", MasterDataDbProperties.DbSchema, t => t.HasCheckConstraint(
                "CK_GeographicalRegionPostalCodeRanges_Form",
                "(\"Prefix\" IS NOT NULL AND \"FromNumeric\" IS NULL AND \"TillNumeric\" IS NULL) OR " +
                "(\"Prefix\" IS NULL AND \"FromNumeric\" IS NOT NULL AND \"TillNumeric\" IS NOT NULL AND \"FromNumeric\" <= \"TillNumeric\")"));
            b.ConfigureByConvention();
            b.Property(p => p.Prefix).HasMaxLength(MasterDataConsts.MaxPostalCodePrefixLength);
            b.HasOne<Country>().WithMany().HasForeignKey(p => p.CountryId).OnDelete(DeleteBehavior.Restrict);
            /* The matcher reads one country's ranges; the list's country filter probes (region, country) per
             * region, and the leading column still serves the cascade. */
            b.HasIndex(p => p.CountryId);
            b.HasIndex(p => new { p.GeographicalRegionId, p.CountryId });
        });
    }

    private static void ConfigureIncompatibility<TPair, TOwner>(ModelBuilder builder, string table)
        where TPair : Incompatibility
        where TOwner : MasterDataAggregateRoot
    {
        builder.Entity<TPair>(b =>
        {
            b.ToTable(table, MasterDataDbProperties.DbSchema, t => t.HasCheckConstraint($"CK_{table}_NotSelf", "\"LeftId\" <> \"RightId\""));
            b.ConfigureByConvention();
            b.HasKey(p => new { p.LeftId, p.RightId });
            b.HasOne<TOwner>().WithMany().HasForeignKey(p => p.LeftId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<TOwner>().WithMany().HasForeignKey(p => p.RightId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(p => p.RightId);
        });
    }
}
