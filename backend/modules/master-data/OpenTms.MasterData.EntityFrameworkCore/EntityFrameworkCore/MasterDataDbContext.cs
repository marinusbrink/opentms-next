using Microsoft.EntityFrameworkCore;
using OpenTms.MasterData.Drivers;
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

    // Run 2 (design 43)
    public DbSet<Relation> Relations { get; set; } = default!;
    public DbSet<RelationAddress> RelationAddresses { get; set; } = default!;
    public DbSet<RelationContactPerson> RelationContactPersons { get; set; } = default!;
    public DbSet<RelationBankAccount> RelationBankAccounts { get; set; } = default!;
    public DbSet<RelationCategoryMembership> RelationCategoryMemberships { get; set; } = default!;
    public DbSet<RelationGeographicalRegion> RelationGeographicalRegions { get; set; } = default!;
    public DbSet<RelationOrganizationUnit> RelationOrganizationUnits { get; set; } = default!;
    public DbSet<RelationExternalIdentifier> RelationExternalIdentifiers { get; set; } = default!;
    public DbSet<RelationTransportCharacteristic> RelationTransportCharacteristics { get; set; } = default!;
    public DbSet<Driver> Drivers { get; set; } = default!;
    public DbSet<DriverQualification> DriverQualifications { get; set; } = default!;

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
            /* The one default country per tenant (PO 2026-10-13), like the currency. */
            b.HasIndex(c => c.TenantId, "IX_Countries_TenantId_Default").IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
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

        ConfigureRelationsAndDrivers(builder);
    }

    /* Run 2 (design 43): Relation with its children and memberships, Driver with qualifications. */
    private static void ConfigureRelationsAndDrivers(ModelBuilder builder)
    {
        /* Trigram index for "contains" on the search key at 20,000 relations (design 43, search index). */
        builder.HasPostgresExtension("pg_trgm");

        builder.Entity<Relation>(b =>
        {
            b.ToTable("Relations", MasterDataDbProperties.DbSchema, t => t.HasCheckConstraint("CK_Relations_Roles", "\"Roles\" > 0 AND \"Roles\" <= 31"));
            b.ConfigureByConvention();
            b.Property(r => r.Code).HasMaxLength(MasterDataConsts.MaxCodeLength).IsRequired();
            b.Property(r => r.Name).HasMaxLength(MasterDataConsts.MaxNameLength).IsRequired();
            b.Property(r => r.FullName).HasMaxLength(MasterDataConsts.MaxNameLength);
            b.Property(r => r.SearchKey).HasMaxLength(MasterDataConsts.MaxSearchKeyLength).IsRequired();
            b.Property(r => r.Phone).HasMaxLength(MasterDataConsts.MaxPhoneLength);
            b.Property(r => r.Mobile).HasMaxLength(MasterDataConsts.MaxPhoneLength);
            b.Property(r => r.Email).HasMaxLength(MasterDataConsts.MaxEmailLength);
            b.Property(r => r.Website).HasMaxLength(MasterDataConsts.MaxWebsiteLength);
            b.Property(r => r.ChamberOfCommerceNumber).HasMaxLength(MasterDataConsts.MaxLegalNumberLength);
            b.Property(r => r.VatNumber).HasMaxLength(MasterDataConsts.MaxLegalNumberLength);
            b.Property(r => r.PreferredCulture).HasMaxLength(MasterDataConsts.MaxCultureLength);
            b.Property(r => r.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.HasMany(r => r.Addresses).WithOne().HasForeignKey(a => a.RelationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(r => r.ContactPersons).WithOne().HasForeignKey(c => c.RelationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(r => r.BankAccounts).WithOne().HasForeignKey(a => a.RelationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(r => r.Categories).WithOne().HasForeignKey(m => m.RelationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(r => r.GeographicalRegions).WithOne().HasForeignKey(m => m.RelationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(r => r.OrganizationUnits).WithOne().HasForeignKey(m => m.RelationId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => new { r.TenantId, r.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            b.HasIndex(r => new { r.TenantId, r.VatNumber }).IsUnique().AreNullsDistinct(false).HasFilter("\"VatNumber\" IS NOT NULL AND " + NotDeleted);
            b.HasIndex(r => new { r.TenantId, r.ChamberOfCommerceNumber }).IsUnique().AreNullsDistinct(false).HasFilter("\"ChamberOfCommerceNumber\" IS NOT NULL AND " + NotDeleted);
            /* Relation administration (PO 2026-10-13). */
            b.Property(r => r.DebtorNumber).HasMaxLength(MasterDataConsts.MaxAccountNumberLength);
            b.Property(r => r.CreditorNumber).HasMaxLength(MasterDataConsts.MaxAccountNumberLength);
            b.Property(r => r.CarrierCode).HasMaxLength(MasterDataConsts.MaxCarrierCodeLength);
            b.HasOne<Currency>().WithMany().HasForeignKey(r => r.CurrencyId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(r => r.CurrencyId);
            b.HasIndex(r => new { r.TenantId, r.DebtorNumber }).IsUnique().AreNullsDistinct(false).HasFilter("\"DebtorNumber\" IS NOT NULL AND " + NotDeleted);
            b.HasIndex(r => new { r.TenantId, r.CreditorNumber }).IsUnique().AreNullsDistinct(false).HasFilter("\"CreditorNumber\" IS NOT NULL AND " + NotDeleted);
            b.HasMany(r => r.ExternalIdentifiers).WithOne().HasForeignKey(e => e.RelationId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(r => r.TransportCharacteristics).WithOne().HasForeignKey(c => c.RelationId).OnDelete(DeleteBehavior.Cascade);
            /* Exactly one own company per tenant (assessment §3.1). */
            b.HasIndex(r => r.TenantId, "IX_Relations_TenantId_OwnCompany").IsUnique().AreNullsDistinct(false).HasFilter("(\"Roles\" & 16) = 16 AND " + NotDeleted);
            b.HasIndex(r => new { r.TenantId, r.SearchKey });
            b.HasIndex(r => r.SearchKey, "IX_Relations_SearchKey_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(r => new { r.TenantId, r.Roles });
        });

        builder.Entity<RelationAddress>(b =>
        {
            b.ToTable("RelationAddresses", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(a => a.Street).HasMaxLength(MasterDataConsts.MaxStreetLength).IsRequired();
            b.Property(a => a.HouseNumber).HasMaxLength(MasterDataConsts.MaxHouseNumberLength);
            b.Property(a => a.HouseNumberSuffix).HasMaxLength(MasterDataConsts.MaxHouseNumberLength);
            b.Property(a => a.AddressLine2).HasMaxLength(MasterDataConsts.MaxAddressLine2Length);
            b.Property(a => a.PostalCode).HasMaxLength(MasterDataConsts.MaxPostalCodeLength).IsRequired();
            b.Property(a => a.PostalCodeKey).HasMaxLength(MasterDataConsts.MaxPostalCodeLength).IsRequired();
            b.Property(a => a.City).HasMaxLength(MasterDataConsts.MaxCityLength).IsRequired();
            b.Property(a => a.TimeZoneId).HasMaxLength(MasterDataConsts.MaxTimeZoneIdLength);
            b.HasOne<AddressType>().WithMany().HasForeignKey(a => a.AddressTypeId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Country>().WithMany().HasForeignKey(a => a.CountryId).OnDelete(DeleteBehavior.Restrict);
            /* One address per address type per relation, and one default address (type null) per relation (design 43, PO 2026-10-12). */
            b.HasIndex(a => new { a.RelationId, a.AddressTypeId }).IsUnique().AreNullsDistinct(false);
            b.HasIndex(a => a.CountryId);
            b.HasIndex(a => a.AddressTypeId);
            /* The matcher: postcode in a country. */
            b.HasIndex(a => new { a.TenantId, a.PostalCodeKey });
        });

        builder.Entity<RelationContactPerson>(b =>
        {
            b.ToTable("RelationContactPersons", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(c => c.FirstName).HasMaxLength(MasterDataConsts.MaxPersonNameLength).IsRequired();
            b.Property(c => c.MiddleName).HasMaxLength(MasterDataConsts.MaxMiddleNameLength);
            b.Property(c => c.LastName).HasMaxLength(MasterDataConsts.MaxPersonNameLength).IsRequired();
            b.Property(c => c.JobTitle).HasMaxLength(MasterDataConsts.MaxJobTitleLength);
            b.Property(c => c.Department).HasMaxLength(MasterDataConsts.MaxJobTitleLength);
            b.Property(c => c.Email).HasMaxLength(MasterDataConsts.MaxEmailLength);
            b.Property(c => c.Phone).HasMaxLength(MasterDataConsts.MaxPhoneLength);
            b.Property(c => c.Mobile).HasMaxLength(MasterDataConsts.MaxPhoneLength);
            b.HasIndex(c => c.RelationId);
            b.HasIndex(c => new { c.TenantId, c.Email });
        });

        builder.Entity<RelationBankAccount>(b =>
        {
            b.ToTable("RelationBankAccounts", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(a => a.Iban).HasMaxLength(MasterDataConsts.MaxIbanLength).IsRequired();
            b.Property(a => a.Bic).HasMaxLength(MasterDataConsts.MaxBicLength);
            b.Property(a => a.BankName).HasMaxLength(MasterDataConsts.MaxBankNameLength);
            b.Property(a => a.AccountHolder).HasMaxLength(MasterDataConsts.MaxAccountHolderLength);
            b.HasIndex(a => new { a.RelationId, a.Iban }).IsUnique();
            b.HasIndex(a => a.RelationId, "IX_RelationBankAccounts_RelationId_Default").IsUnique().HasFilter("\"IsDefault\" = true");
        });

        builder.Entity<RelationCategoryMembership>(b =>
        {
            b.ToTable("RelationCategoryMemberships", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(m => new { m.RelationId, m.RelationCategoryId });
            b.HasOne<RelationCategory>().WithMany().HasForeignKey(m => m.RelationCategoryId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(m => m.RelationCategoryId);
        });

        builder.Entity<RelationGeographicalRegion>(b =>
        {
            b.ToTable("RelationGeographicalRegions", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(m => new { m.RelationId, m.GeographicalRegionId });
            b.HasOne<GeographicalRegion>().WithMany().HasForeignKey(m => m.GeographicalRegionId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(m => m.GeographicalRegionId);
        });

        builder.Entity<RelationExternalIdentifier>(b =>
        {
            b.ToTable("RelationExternalIdentifiers", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(e => e.Source).HasMaxLength(MasterDataConsts.MaxExternalSourceLength).IsRequired();
            b.Property(e => e.Identifier).HasMaxLength(MasterDataConsts.MaxExternalIdentifierLength).IsRequired();
            b.HasIndex(e => e.RelationId);
            /* One relation per (source, identifier) in a tenant: the key other systems use to find the relation. */
            b.HasIndex(e => new { e.TenantId, e.Source, e.Identifier }).IsUnique().AreNullsDistinct(false);
        });

        builder.Entity<RelationTransportCharacteristic>(b =>
        {
            b.ToTable("RelationTransportCharacteristics", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(m => new { m.RelationId, m.TransportCharacteristicsId });
            b.HasOne<TransportCharacteristics>().WithMany().HasForeignKey(m => m.TransportCharacteristicsId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(m => m.TransportCharacteristicsId);
        });

        builder.Entity<RelationOrganizationUnit>(b =>
        {
            b.ToTable("RelationOrganizationUnits", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(m => new { m.RelationId, m.OrganizationUnitId });
            b.HasIndex(m => m.OrganizationUnitId);
        });

        builder.Entity<Driver>(b =>
        {
            b.ToTable("Drivers", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(d => d.Code).HasMaxLength(MasterDataConsts.MaxCodeLength).IsRequired();
            b.Property(d => d.FirstName).HasMaxLength(MasterDataConsts.MaxPersonNameLength).IsRequired();
            b.Property(d => d.MiddleName).HasMaxLength(MasterDataConsts.MaxMiddleNameLength);
            b.Property(d => d.LastName).HasMaxLength(MasterDataConsts.MaxPersonNameLength).IsRequired();
            b.Property(d => d.Initials).HasMaxLength(MasterDataConsts.MaxInitialsLength);
            b.Property(d => d.SearchKey).HasMaxLength(MasterDataConsts.MaxSearchKeyLength).IsRequired();
            b.Property(d => d.Phone).HasMaxLength(MasterDataConsts.MaxPhoneLength);
            b.Property(d => d.Mobile).HasMaxLength(MasterDataConsts.MaxPhoneLength);
            b.Property(d => d.Email).HasMaxLength(MasterDataConsts.MaxEmailLength);
            b.Property(d => d.PreferredCulture).HasMaxLength(MasterDataConsts.MaxCultureLength);
            b.Property(d => d.Description).HasMaxLength(MasterDataConsts.MaxDescriptionLength);
            b.Ignore(d => d.FullName);
            b.HasOne<Relation>().WithMany().HasForeignKey(d => d.EmployerRelationId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(d => d.Qualifications).WithOne().HasForeignKey(q => q.DriverId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(d => d.TenantId);
            b.HasIndex(d => new { d.TenantId, d.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            b.HasIndex(d => new { d.TenantId, d.UserId }).IsUnique().AreNullsDistinct(false).HasFilter("\"UserId\" IS NOT NULL AND " + NotDeleted);
            b.HasIndex(d => new { d.TenantId, d.SearchKey });
            b.HasIndex(d => d.EmployerRelationId);
        });

        builder.Entity<DriverQualification>(b =>
        {
            b.ToTable("DriverQualifications", MasterDataDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(q => q.Number).HasMaxLength(MasterDataConsts.MaxQualificationNumberLength);
            b.Property(q => q.Description).HasMaxLength(MasterDataConsts.MaxQualificationDescriptionLength);
            b.HasIndex(q => q.DriverId);
            b.HasIndex(q => new { q.TenantId, q.ValidUntil });
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
