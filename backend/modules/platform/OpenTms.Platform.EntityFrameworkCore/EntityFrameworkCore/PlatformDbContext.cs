using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.Maintenance.Instances;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;

namespace OpenTms.Platform.EntityFrameworkCore;

[ConnectionStringName(PlatformDbProperties.ConnectionStringName)]
public class PlatformDbContext : AbpDbContext<PlatformDbContext>, IHasEventOutbox
{
    /* Add DbSet properties for the Platform module's aggregate roots / entities here. */

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
    }
}
