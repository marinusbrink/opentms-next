using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.Grid;
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
    }
}
