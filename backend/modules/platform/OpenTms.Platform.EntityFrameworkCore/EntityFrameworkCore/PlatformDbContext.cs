using Microsoft.EntityFrameworkCore;
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
    }
}
