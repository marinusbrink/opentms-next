using Microsoft.EntityFrameworkCore;
using OpenTms.Integrations.Routing;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.Integrations.EntityFrameworkCore;

[ConnectionStringName(IntegrationsDbProperties.ConnectionStringName)]
public class IntegrationsDbContext : AbpDbContext<IntegrationsDbContext>
{
    /// <summary>The route cache of design 60: integrations."RouteCache", one row per (tenant, key), expiring rows.</summary>
    public DbSet<RouteCacheEntry> RouteCache { get; set; } = default!;

    public IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("integrations").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(IntegrationsDbProperties.DbSchema);

        builder.Entity<RouteCacheEntry>(b =>
        {
            b.ToTable("RouteCache", IntegrationsDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(e => e.Key).IsRequired().HasMaxLength(RoutingConsts.MaxKeyLength);
            b.Property(e => e.Provider).IsRequired().HasMaxLength(RoutingConsts.MaxProviderLength);
            b.Property(e => e.TollAmount).HasPrecision(RoutingConsts.MoneyPrecision, RoutingConsts.MoneyScale);
            b.Property(e => e.TollCurrencyCode).HasMaxLength(RoutingConsts.MaxCurrencyCodeLength);
            b.Property(e => e.Co2Kg).HasPrecision(RoutingConsts.Co2Precision, RoutingConsts.Co2Scale);
            /* The lookup of every routing call. NULLS NOT DISTINCT so host-level rows are unique too. */
            b.HasIndex(e => new { e.TenantId, e.Key }).IsUnique().AreNullsDistinct(false);
            /* The daily purge of expired rows. */
            b.HasIndex(e => e.ExpiresAt);
        });
    }
}
