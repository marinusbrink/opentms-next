using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.DataProtection;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.Instances;
using OpenTms.Platform.NumberSequences;
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
    }
}
