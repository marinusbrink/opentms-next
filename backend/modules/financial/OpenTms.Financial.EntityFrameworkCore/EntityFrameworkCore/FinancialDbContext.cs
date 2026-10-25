using Microsoft.EntityFrameworkCore;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.EntityFrameworkCore.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.Financial.EntityFrameworkCore;

[ConnectionStringName(FinancialDbProperties.ConnectionStringName)]
public class FinancialDbContext : AbpDbContext<FinancialDbContext>
{
    /* Invoicing (design 52). */
    public DbSet<VatPercentage> VatPercentages { get; set; } = default!;
    public DbSet<VatPercentageRate> VatPercentageRates { get; set; } = default!;
    public DbSet<OutboundInvoiceType> OutboundInvoiceTypes { get; set; } = default!;
    public DbSet<InboundInvoiceType> InboundInvoiceTypes { get; set; } = default!;

    /* Tariffs (design 52). */
    public DbSet<AdditionalService> AdditionalServices { get; set; } = default!;
    public DbSet<DiscountOrSurcharge> DiscountsOrSurcharges { get; set; } = default!;
    public DbSet<DiscountOrSurchargeValue> DiscountOrSurchargeValues { get; set; } = default!;
    public DbSet<DiscountOrSurchargeRegion> DiscountOrSurchargeRegions { get; set; } = default!;

    public FinancialDbContext(DbContextOptions<FinancialDbContext> options)
        : base(options)
    {
    }

    /* Every unique key is per tenant with NULLS NOT DISTINCT and ignores soft-deleted rows (design 41's rule). */
    private const string NotDeleted = "\"IsDeleted\" = false";

    /* Table names carry the submodule as prefix (backend/modules/financial/README.md). */
    private const string Invoicing = "Invoicing";
    private const string Tariffs = "Tariffs";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("financial").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(FinancialDbProperties.DbSchema);

        ConfigureInvoicing(builder);
        ConfigureTariffs(builder);
    }

    private static void ConfigureInvoicing(ModelBuilder builder)
    {
        builder.Entity<VatPercentage>(b =>
        {
            b.ToTable(Invoicing + "VatPercentages", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(v => v.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(v => v.Name).HasLocalizedNameColumn();
            b.Property(v => v.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.Property(v => v.CountryCode).HasMaxLength(ReferenceDataConsts.IsoAlpha2Length).IsRequired();
            b.Property(v => v.SalesLedgerAccount).HasMaxLength(ReferenceDataConsts.MaxLedgerAccountLength);
            b.Property(v => v.PurchaseLedgerAccount).HasMaxLength(ReferenceDataConsts.MaxLedgerAccountLength);
            b.HasMany(v => v.Rates).WithOne().HasForeignKey(r => r.VatPercentageId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(v => v.TenantId);
            b.HasIndex(v => new { v.TenantId, v.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* The one default per tenant (assessment §3.8). */
            b.HasIndex(v => v.TenantId, "IX_InvoicingVatPercentages_TenantId_Default").IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });

        builder.Entity<VatPercentageRate>(b =>
        {
            b.ToTable(Invoicing + "VatPercentageRates", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(r => r.Percentage).HasPrecision(FinancialConsts.VatPercentagePrecision, FinancialConsts.VatPercentageScale);
            b.HasIndex(r => r.TenantId);
            /* One rate per category and date (§3.4). */
            b.HasIndex(r => new { r.VatPercentageId, r.ValidFrom }).IsUnique();
        });

        builder.Entity<OutboundInvoiceType>(b =>
        {
            b.ToTable(Invoicing + "OutboundInvoiceTypes", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(t => t.Name).HasLocalizedNameColumn();
            b.Property(t => t.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.HasNumbering(t => t.Numbering);
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* One default per kind per tenant (Assumption 5). */
            b.HasIndex(t => new { t.TenantId, t.Kind }, "IX_InvoicingOutboundInvoiceTypes_TenantId_Kind_Default").IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });

        builder.Entity<InboundInvoiceType>(b =>
        {
            b.ToTable(Invoicing + "InboundInvoiceTypes", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(t => t.Name).HasMaxLength(ReferenceDataConsts.MaxNameLength).IsRequired();
            b.Property(t => t.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.Property(t => t.CostLedgerAccount).HasMaxLength(ReferenceDataConsts.MaxLedgerAccountLength);
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });
    }

    private static void ConfigureTariffs(ModelBuilder builder)
    {
        builder.Entity<AdditionalService>(b =>
        {
            b.ToTable(Tariffs + "AdditionalServices", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(s => s.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(s => s.Name).HasMaxLength(ReferenceDataConsts.MaxNameLength).IsRequired();
            b.Property(s => s.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.Property(s => s.RevenueLedgerAccount).HasMaxLength(ReferenceDataConsts.MaxLedgerAccountLength);
            b.Property(s => s.CostLedgerAccount).HasMaxLength(ReferenceDataConsts.MaxLedgerAccountLength);
            /* Restrict: a VAT category in use is refused by the service first (InUse); the FK index serves that check. */
            b.HasOne<VatPercentage>().WithMany().HasForeignKey(s => s.VatPercentageId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(s => s.TenantId);
            b.HasIndex(s => new { s.TenantId, s.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<DiscountOrSurcharge>(b =>
        {
            b.ToTable(Tariffs + "DiscountsOrSurcharges", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(d => d.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(d => d.Name).HasMaxLength(ReferenceDataConsts.MaxNameLength).IsRequired();
            b.Property(d => d.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.Property(d => d.LedgerAccount).HasMaxLength(ReferenceDataConsts.MaxLedgerAccountLength);
            b.HasOne<VatPercentage>().WithMany().HasForeignKey(d => d.VatPercentageId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(d => d.Values).WithOne().HasForeignKey(v => v.DiscountOrSurchargeId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(d => d.Regions).WithOne().HasForeignKey(r => r.DiscountOrSurchargeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(d => d.TenantId);
            b.HasIndex(d => new { d.TenantId, d.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<DiscountOrSurchargeValue>(b =>
        {
            b.ToTable(Tariffs + "DiscountOrSurchargeValues", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(v => v.Value).HasPrecision(FinancialConsts.ValuePrecision, FinancialConsts.ValueScale);
            b.Property(v => v.CurrencyCode).HasMaxLength(ReferenceDataConsts.IsoCurrencyCodeLength);
            b.HasIndex(v => v.TenantId);
            /* One value per discount or surcharge and date (§3.4). */
            b.HasIndex(v => new { v.DiscountOrSurchargeId, v.ValidFrom }, "IX_TariffsDiscountOrSurchargeValues_Parent_ValidFrom").IsUnique();
        });

        builder.Entity<DiscountOrSurchargeRegion>(b =>
        {
            b.ToTable(Tariffs + "DiscountOrSurchargeRegions", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(r => r.TenantId);
            /* Each region once per discount or surcharge; GeographicalRegionId is a Master Data id (no cross-schema FK). */
            b.HasIndex(r => new { r.DiscountOrSurchargeId, r.GeographicalRegionId }, "IX_TariffsDiscountOrSurchargeRegions_Parent_Region").IsUnique();
        });
    }
}
