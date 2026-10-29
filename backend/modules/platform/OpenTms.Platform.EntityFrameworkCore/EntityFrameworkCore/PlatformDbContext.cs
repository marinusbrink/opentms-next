using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.Accounts;
using OpenTms.Platform.DataProtection;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.DocumentTypes;
using OpenTms.Platform.Editions;
using OpenTms.Platform.EmailTemplates;
using OpenTms.Platform.EntityFrameworkCore.Localization;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.Instances;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReportDefinitions;
using OpenTms.Platform.SignIn;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.Platform.EntityFrameworkCore;

[ConnectionStringName(PlatformDbProperties.ConnectionStringName)]
public class PlatformDbContext : AbpDbContext<PlatformDbContext>, IHasEventOutbox
{
    /* Add DbSet properties for the Platform module's aggregate roots / entities here. */

    /// <summary>Per-user grid layouts, one row per (tenant, user, grid): docs/designs/grid-settings-storage.md.</summary>
    public DbSet<GridUserSetting> GridUserSettings { get; set; } = default!;

    /// <summary>
    /// The transactional outbox: platform."AbpEventOutbox", present in the host database and in every
    /// tenant database, so an event is committed in the same transaction and the same database as
    /// the fact it describes (AD-16). Never read or write this table yourself — publish through
    /// IDistributedEventBus; the OutboxDispatcher drains it.
    /// </summary>
    public DbSet<OutgoingEventRecord> OutgoingEvents { get; set; } = default!;

    /// <summary>
    /// The instance registry: platform."ApplicationInstances", written in the host database only
    /// (the table exists in every database because the schema is shared; tenant databases keep it empty).
    /// </summary>
    public DbSet<ApplicationInstance> ApplicationInstances { get; set; } = default!;

    /// <summary>
    /// Editions: platform."Editions", host-owned and written in the host database only (tenant databases keep
    /// the table empty). Feature values per edition are FeatureManagement's (docs/designs/tenants-and-editions.md).
    /// </summary>
    public DbSet<Edition> Editions { get; set; } = default!;

    /// <summary>
    /// ASP.NET Core data-protection keys: platform."DataProtectionKeys", written and read in the host database only
    /// (tenant databases keep the table empty), so every instance protects cookies and tickets with the same keys
    /// (docs/designs/tenants-and-editions.md, "Shared data-protection keys").
    /// </summary>
    public DbSet<DataProtectionKeyRecord> DataProtectionKeys { get; set; } = default!;

    /// <summary>
    /// The sign-in lookup: platform."UserSignInLookups", hashed e-mail addresses to the user's tenant,
    /// written and read in the host database only (tenant databases keep the table empty;
    /// docs/designs/62-sign-in-tenant-detection.md).
    /// </summary>
    public DbSet<UserSignInLookup> UserSignInLookups { get; set; } = default!;

    /// <summary>
    /// Remembered account switches of the user menu: platform."UserAccountLinks", host database only, like the sign-in
    /// lookup (docs/designs/63-user-menu.md).
    /// </summary>
    public DbSet<UserAccountLink> UserAccountLinks { get; set; } = default!;

    // Platform master data (docs/designs/54-platform-master-data.md)
    public DbSet<EmailTemplate> EmailTemplates { get; set; } = default!;
    public DbSet<ReportDefinition> ReportDefinitions { get; set; } = default!;
    public DbSet<DocumentType> DocumentTypes { get; set; } = default!;
    public DbSet<DocumentTypeTransformation> DocumentTypeTransformations { get; set; } = default!;

    /// <summary>
    /// Number-sequence counters: platform."NumberSequenceCounters", one row per (tenant, sequence, period), written
    /// only by the atomic upsert of <see cref="EfCoreNumberSequenceCounterStore"/> inside the numbered fact's
    /// transaction (docs/designs/50-platform-reference-data-and-number-sequences.md).
    /// </summary>
    public DbSet<NumberSequenceCounter> NumberSequenceCounters { get; set; } = default!;

    /// <summary>
    /// Derived-value states: platform."DerivedValueStates", one row per (tenant, target type, target id), written only by
    /// the atomic statements of <see cref="EfCoreDerivedValueStateStore"/> (docs/designs/49-derived-values-and-live-updates.md).
    /// </summary>
    public DbSet<DerivedValueState> DerivedValueStates { get; set; } = default!;

    public PlatformDbContext(DbContextOptions<PlatformDbContext> options)
        : base(options)
    {
    }

    /* Every unique key of the master-data lists is per tenant with NULLS NOT DISTINCT and ignores soft-deleted rows, so
     * a deleted row frees its key (as Master Data). The filter is PostgreSQL syntax; SQLite (unit tests) accepts it. */
    private const string NotDeleted = "\"IsDeleted\" = false";

    /* The document types' names are LocalizedName jsonb columns that the lists sort and search through
     * LocalizedNameQuery. Enabled here rather than per provider configuration, so every configuration of this context
     * (runtime, DbMigrator, the SQLite and PostgreSQL test modules) translates it; the call is idempotent. */
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseLocalizedNameTranslation();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("platform").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(PlatformDbProperties.DbSchema);

        builder.ConfigureEventOutbox();

        builder.Entity<GridUserSetting>(b =>
        {
            b.ToTable("GridUserSettings");
            b.ConfigureByConvention();

            b.Property(x => x.GridId).IsRequired().HasMaxLength(GridUserSettingConsts.MaxGridIdLength);
            b.Property(x => x.SettingsJson).IsRequired(); // text: no length limit (the defect design #6's blob had)

            /* One row per (tenant, user, grid). NULLS NOT DISTINCT so host users (TenantId null) get the
             * same hard guarantee as tenant users; PostgreSQL 15+, other providers ignore the annotation. */
            b.HasIndex(x => new { x.TenantId, x.UserId, x.GridId })
                .IsUnique()
                .AreNullsDistinct(false);
        });

        builder.Entity<ApplicationInstance>(b =>
        {
            b.ToTable("ApplicationInstances", PlatformDbProperties.DbSchema);
            b.Property(i => i.Role).HasMaxLength(32).IsRequired();
            b.Property(i => i.MachineName).HasMaxLength(256).IsRequired();
            b.Property(i => i.Version).HasMaxLength(256).IsRequired();
            b.Property(i => i.Environment).HasMaxLength(64).IsRequired();
            b.Property(i => i.RuntimeVersion).HasMaxLength(128).IsRequired();
            b.Property(i => i.HealthStatus).HasMaxLength(16);
            b.Property(i => i.HealthDetails).HasColumnType("jsonb");
            /* The list reads newest heartbeat first; housekeeping deletes by it. */
            b.HasIndex(i => i.LastHeartbeatAt);
            b.HasIndex(i => i.Role);
        });

        builder.Entity<Edition>(b =>
        {
            b.ToTable("Editions", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(e => e.DisplayName).IsRequired().HasMaxLength(EditionConsts.MaxDisplayNameLength);
            /* Unique among editions that are not deleted: a soft-deleted name may be reused. */
            b.HasIndex(e => e.DisplayName).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        builder.Entity<DataProtectionKeyRecord>(b =>
        {
            b.ToTable("DataProtectionKeys", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(k => k.FriendlyName).HasMaxLength(DataProtectionKeyRecord.MaxFriendlyNameLength);
            b.Property(k => k.Xml).IsRequired();
        });

        builder.Entity<UserSignInLookup>(b =>
        {
            b.ToTable("UserSignInLookups", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(l => l.Id).ValueGeneratedNever();
            b.Property(l => l.EmailHash).IsRequired().HasMaxLength(UserSignInLookup.HashLength);
            /* The sign-in page looks a typed address up; the rebuild replaces a tenant's rows. */
            b.HasIndex(l => l.EmailHash);
            b.HasIndex(l => l.UserTenantId);
        });

        builder.Entity<UserAccountLink>(b =>
        {
            b.ToTable("UserAccountLinks", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(l => l.LinkedSecurityStamp).IsRequired().HasMaxLength(UserAccountLink.MaxSecurityStampLength);
            /* One link per (user, other tenant); NULLS NOT DISTINCT so links to and from the host are unique too. */
            b.HasIndex(l => new { l.UserTenantId, l.UserId, l.LinkedTenantId }).IsUnique().AreNullsDistinct(false);
            b.HasIndex(l => l.LinkedTenantId);
        });

        builder.Entity<NumberSequenceCounter>(b =>
        {
            b.ToTable("NumberSequenceCounters", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(c => c.SequenceKey).IsRequired().HasMaxLength(NumberSequenceCounter.MaxSequenceKeyLength);
            b.Property(c => c.Period).IsRequired().HasMaxLength(NumberSequenceFormat.MaxPeriodLength);
            /* The conflict target of the upsert. NULLS NOT DISTINCT so host-level counters are unique too. */
            b.HasIndex(c => new { c.TenantId, c.SequenceKey, c.Period }).IsUnique().AreNullsDistinct(false);
        });

        builder.Entity<DerivedValueState>(b =>
        {
            b.ToTable("DerivedValueStates", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(s => s.TargetType).IsRequired().HasMaxLength(DerivedValueConsts.MaxTargetTypeLength);
            b.Property(s => s.LastErrorCode).HasMaxLength(DerivedValueConsts.MaxErrorCodeLength);
            /* The conflict target of the request upsert. NULLS NOT DISTINCT so host-level records are unique too. */
            b.HasIndex(s => new { s.TenantId, s.TargetType, s.TargetId }).IsUnique().AreNullsDistinct(false);
            /* "What is still pending or failed" for maintenance and bulk re-runs. */
            b.HasIndex(s => new { s.TenantId, s.TargetType, s.Status });
        });

        ConfigureMasterData(builder);
    }

    /* Platform master data (docs/designs/54-platform-master-data.md, "Migration strategy"). */
    private static void ConfigureMasterData(ModelBuilder builder)
    {
        builder.Entity<EmailTemplate>(b =>
        {
            b.ToTable("EmailTemplates", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Name).HasMaxLength(PlatformMasterDataConsts.MaxNameLength).IsRequired();
            b.Property(t => t.NormalizedName).HasMaxLength(PlatformMasterDataConsts.MaxNameLength).IsRequired();
            b.Property(t => t.Culture).HasMaxLength(PlatformMasterDataConsts.MaxCultureLength);
            b.Property(t => t.SenderAddress).HasMaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength);
            b.Property(t => t.ReplyTo).HasMaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength);
            b.Property(t => t.To).HasMaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength);
            b.Property(t => t.Cc).HasMaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength);
            b.Property(t => t.Bcc).HasMaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength);
            b.Property(t => t.Subject).HasMaxLength(PlatformMasterDataConsts.MaxSubjectLength).IsRequired();
            b.Property(t => t.HtmlBody).IsRequired(); // text; bounded to 200,000 characters by the DTO and the aggregate
            b.Property(t => t.Description).HasMaxLength(PlatformMasterDataConsts.MaxDescriptionLength);
            /* Names are unique regardless of case (PO 2026-10-16). */
            b.HasIndex(t => new { t.TenantId, t.NormalizedName }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* At most one default per purpose (assessment §3.8). */
            b.HasIndex(t => new { t.TenantId, t.Purpose }, "IX_EmailTemplates_TenantId_Purpose_Default")
                .IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });

        builder.Entity<ReportDefinition>(b =>
        {
            b.ToTable("ReportDefinitions", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(d => d.Name).HasMaxLength(PlatformMasterDataConsts.MaxNameLength).IsRequired();
            b.Property(d => d.NormalizedName).HasMaxLength(PlatformMasterDataConsts.MaxNameLength).IsRequired();
            b.Property(d => d.Culture).HasMaxLength(PlatformMasterDataConsts.MaxCultureLength).IsRequired();
            b.Property(d => d.ProviderTemplateId).HasMaxLength(PlatformMasterDataConsts.MaxProviderTemplateIdLength).IsRequired();
            b.Property(d => d.FileNamePattern).HasMaxLength(PlatformMasterDataConsts.MaxFileNamePatternLength).IsRequired();
            b.Property(d => d.Description).HasMaxLength(PlatformMasterDataConsts.MaxDescriptionLength);
            b.HasIndex(d => new { d.TenantId, d.NormalizedName }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* At most one default per purpose and language. */
            b.HasIndex(d => new { d.TenantId, d.Purpose, d.Culture }, "IX_ReportDefinitions_TenantId_Purpose_Culture_Default")
                .IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });

        builder.Entity<DocumentType>(b =>
        {
            b.ToTable("DocumentTypes", PlatformDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Code).HasMaxLength(PlatformMasterDataConsts.MaxCodeLength).IsRequired();
            b.Property(t => t.Name).HasLocalizedNameColumn();
            b.Property(t => t.Description).HasMaxLength(PlatformMasterDataConsts.MaxDescriptionLength);
            b.HasMany(t => t.Transformations).WithOne().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Cascade);
            /* The natural key the seed upserts on. */
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<DocumentTypeTransformation>(b =>
        {
            b.ToTable("DocumentTypeTransformations", PlatformDbProperties.DbSchema, t => t.HasCheckConstraint(
                "CK_DocumentTypeTransformations_Position",
                "\"Position\" >= 1 AND \"Position\" <= " + PlatformMasterDataConsts.MaxDocumentTypeTransformations));
            b.ConfigureByConvention();
            /* Each kind at most once per document type. */
            b.HasKey(x => new { x.DocumentTypeId, x.Kind });
            b.Property(x => x.Argument).HasMaxLength(PlatformMasterDataConsts.MaxTransformationArgumentLength);
        });
    }
}
