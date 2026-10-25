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

    /* Tariffs (design 57): price lists, rate lines. */
    public DbSet<PriceList> PriceLists { get; set; } = default!;
    public DbSet<PriceListColumn> PriceListColumns { get; set; } = default!;
    public DbSet<PriceListRule> PriceListRules { get; set; } = default!;
    public DbSet<PriceListBracket> PriceListBrackets { get; set; } = default!;
    public DbSet<PriceListCell> PriceListCells { get; set; } = default!;
    public DbSet<RateLine> RateLines { get; set; } = default!;

    /* Invoicing (design 57): relation financial settings. */
    public DbSet<RelationFinancialSettings> RelationFinancialSettings { get; set; } = default!;
    public DbSet<RelationPriceList> RelationPriceLists { get; set; } = default!;
    public DbSet<RelationDiscountOrSurcharge> RelationDiscountsOrSurcharges { get; set; } = default!;

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
        ConfigurePriceLists(builder);
        ConfigureRateLines(builder);
        ConfigureRelationFinancialSettings(builder);
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
            /* Design 57: the second pass and the per-kilometre modifiers. */
            b.Property(d => d.BillableKilometrePercentage).HasPrecision(FinancialConsts.VatPercentagePrecision, FinancialConsts.VatPercentageScale).HasDefaultValue(100m);
            b.Property(d => d.KilometreBasis).HasDefaultValue(KilometreBasis.Distance);
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

    /* Design 57: a list with its columns; the rules as their own rows (10,000 per list) with brackets and cells beneath. */
    private static void ConfigurePriceLists(ModelBuilder builder)
    {
        builder.Entity<PriceList>(b =>
        {
            b.ToTable(Tariffs + "PriceLists", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(p => p.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(p => p.Name).HasMaxLength(ReferenceDataConsts.MaxNameLength).IsRequired();
            b.Property(p => p.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.Property(p => p.CurrencyCode).HasMaxLength(ReferenceDataConsts.IsoCurrencyCodeLength).IsRequired();
            b.Property(p => p.UnitConversionFactor).HasPrecision(FinancialConsts.FactorPrecision, FinancialConsts.FactorScale);
            b.Ignore(p => p.ColumnCount);
            b.HasMany(p => p.Columns).WithOne().HasForeignKey(c => c.PriceListId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(p => p.TenantId);
            b.HasIndex(p => new { p.TenantId, p.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<PriceListColumn>(b =>
        {
            b.ToTable(Tariffs + "PriceListColumns", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(c => new { c.PriceListId, c.Position });
            b.Property(c => c.UpperBound).HasPrecision(FinancialConsts.ValuePrecision, FinancialConsts.ValueScale);
            b.HasIndex(c => c.TenantId);
        });

        builder.Entity<PriceListRule>(b =>
        {
            b.ToTable(Tariffs + "PriceListRules", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(r => r.MinAmount).HasPrecision(FinancialConsts.AmountPrecision, FinancialConsts.AmountScale);
            b.Property(r => r.MaxAmount).HasPrecision(FinancialConsts.AmountPrecision, FinancialConsts.AmountScale);
            b.Ignore(r => r.Tier);
            b.Ignore(r => r.HasFromSide);
            b.Ignore(r => r.HasToSide);
            b.Ignore(r => r.OpenBracket);
            b.HasOne<PriceList>().WithMany().HasForeignKey(r => r.PriceListId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(r => r.Brackets).WithOne().HasForeignKey(k => k.PriceListRuleId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(r => r.TenantId);
            /* The rule page and the resolver read a list's rules in position order. */
            b.HasIndex(r => new { r.PriceListId, r.Position }, "IX_TariffsPriceListRules_PriceListId_Position");
        });

        builder.Entity<PriceListBracket>(b =>
        {
            b.ToTable(Tariffs + "PriceListBrackets", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(k => k.UpperBound).HasPrecision(FinancialConsts.ValuePrecision, FinancialConsts.ValueScale);
            b.HasMany(k => k.Cells).WithOne().HasForeignKey(c => c.PriceListBracketId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(k => k.TenantId);
            b.HasIndex(k => new { k.PriceListRuleId, k.Position }, "IX_TariffsPriceListBrackets_RuleId_Position").IsUnique();
        });

        builder.Entity<PriceListCell>(b =>
        {
            b.ToTable(Tariffs + "PriceListCells", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(c => new { c.PriceListBracketId, c.ColumnPosition });
            b.Property(c => c.UnitPrice).HasPrecision(FinancialConsts.ValuePrecision, FinancialConsts.ValueScale);
            b.HasIndex(c => c.TenantId);
        });
    }

    /* Design 57, AD-08, AP-07: one table for both sides, exactly one anchor (check constraint), ids without cross-module keys. */
    private static void ConfigureRateLines(ModelBuilder builder)
    {
        builder.Entity<RateLine>(b =>
        {
            b.ToTable(Tariffs + "RateLines", FinancialDbProperties.DbSchema, t => t.HasCheckConstraint(
                "CK_TariffsRateLines_OneAnchor",
                "(CASE WHEN \"OrderId\" IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN \"OrderLineId\" IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN \"LegId\" IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN \"StopId\" IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN \"TripId\" IS NOT NULL THEN 1 ELSE 0 END) = 1"));
            b.ConfigureByConvention();
            b.Property(l => l.DescriptionKey).HasMaxLength(FinancialConsts.MaxDescriptionKeyLength);
            b.Property(l => l.Description).HasMaxLength(FinancialConsts.MaxRateLineDescriptionLength);
            b.Property(l => l.Quantity).HasPrecision(FinancialConsts.ValuePrecision, FinancialConsts.ValueScale);
            b.Property(l => l.UnitPrice).HasPrecision(FinancialConsts.ValuePrecision, FinancialConsts.ValueScale);
            b.Property(l => l.Amount).HasPrecision(FinancialConsts.AmountPrecision, FinancialConsts.AmountScale);
            b.Property(l => l.CurrencyCode).HasMaxLength(ReferenceDataConsts.IsoCurrencyCodeLength).IsRequired();
            b.Ignore(l => l.Anchor);
            b.HasIndex(l => l.TenantId);
            b.HasIndex(l => new { l.TenantId, l.AnchorOrderId }, "IX_TariffsRateLines_TenantId_AnchorOrderId");
            b.HasIndex(l => new { l.TenantId, l.AnchorTripId }, "IX_TariffsRateLines_TenantId_AnchorTripId");
            b.HasIndex(l => new { l.TenantId, l.LegId }, "IX_TariffsRateLines_TenantId_LegId");
            /* The in-use check of a price list delete. */
            b.HasIndex(l => l.PriceListId);
        });
    }

    /* Design 57: the financial defaults of a relation, keyed by the Master Data relation id. */
    private static void ConfigureRelationFinancialSettings(ModelBuilder builder)
    {
        builder.Entity<RelationFinancialSettings>(b =>
        {
            b.ToTable(Invoicing + "RelationFinancialSettings", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(s => s.CurrencyCode).HasMaxLength(ReferenceDataConsts.IsoCurrencyCodeLength);
            b.HasOne<VatPercentage>().WithMany().HasForeignKey(s => s.VatPercentageId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(s => s.PriceLists).WithOne().HasForeignKey(p => p.RelationFinancialSettingsId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(s => s.DiscountsOrSurcharges).WithOne().HasForeignKey(d => d.RelationFinancialSettingsId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(s => s.TenantId);
            b.HasIndex(s => new { s.TenantId, s.RelationId }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<RelationPriceList>(b =>
        {
            b.ToTable(Invoicing + "RelationPriceLists", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            /* Restrict: a list in use by a relation is refused by the service first (PriceListInUse); the FK index serves that check. */
            b.HasOne<PriceList>().WithMany().HasForeignKey(p => p.PriceListId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(p => p.TenantId);
            b.HasIndex(p => new { p.RelationFinancialSettingsId, p.Side, p.PriceListId }, "IX_InvoicingRelationPriceLists_Settings_Side_PriceList").IsUnique();
        });

        builder.Entity<RelationDiscountOrSurcharge>(b =>
        {
            b.ToTable(Invoicing + "RelationDiscountsOrSurcharges", FinancialDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasOne<DiscountOrSurcharge>().WithMany().HasForeignKey(d => d.DiscountOrSurchargeId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(d => d.TenantId);
            b.HasIndex(d => new { d.RelationFinancialSettingsId, d.DiscountOrSurchargeId }, "IX_InvoicingRelationDiscountsOrSurcharges_Settings_Item").IsUnique();
        });
    }
}
