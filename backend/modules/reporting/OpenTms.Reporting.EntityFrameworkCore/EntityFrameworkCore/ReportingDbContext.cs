using Microsoft.EntityFrameworkCore;
using OpenTms.Reporting.Facts;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.Reporting.EntityFrameworkCore;

[ConnectionStringName(ReportingDbProperties.ConnectionStringName)]
public class ReportingDbContext : AbpDbContext<ReportingDbContext>
{
    private const int MoneyPrecision = 18;
    private const int MoneyScale = 2;
    private const int Co2Scale = 3;

    public DbSet<OrderFact> OrderFacts { get; set; } = default!;

    public DbSet<TripFact> TripFacts { get; set; } = default!;

    public ReportingDbContext(DbContextOptions<ReportingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("reporting").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(ReportingDbProperties.DbSchema);

        /* Design 75: the columns' precision follows the source's; the indexes are the design's list, the volume test decides. */
        builder.Entity<OrderFact>(b =>
        {
            b.ToTable("OrderFacts", ReportingDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(f => f.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(f => f.RevenueAmount).HasPrecision(MoneyPrecision, MoneyScale);
            b.Property(f => f.CostOfRevenueAmount).HasPrecision(MoneyPrecision, MoneyScale);
            b.Property(f => f.MarginAmount).HasPrecision(MoneyPrecision, MoneyScale);
            b.Property(f => f.Co2Kg).HasPrecision(MoneyPrecision, Co2Scale);
            b.HasIndex(f => new { f.TenantId, f.OrderDate });
            b.HasIndex(f => new { f.TenantId, f.CustomerRelationId, f.OrderDate });
            b.HasIndex(f => new { f.TenantId, f.TransportOrderTypeId, f.OrderDate });
            b.HasIndex(f => new { f.TenantId, f.OrganizationUnitId, f.OrderDate });
        });

        builder.Entity<TripFact>(b =>
        {
            b.ToTable("TripFacts", ReportingDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(f => f.CostAmount).HasPrecision(MoneyPrecision, MoneyScale);
            b.Property(f => f.RevenueAmount).HasPrecision(MoneyPrecision, MoneyScale);
            b.Property(f => f.Co2Kg).HasPrecision(MoneyPrecision, Co2Scale);
            b.Property(f => f.LoadFactor).HasPrecision(9, 4);
            b.HasIndex(f => new { f.TenantId, f.TripDate });
            b.HasIndex(f => new { f.TenantId, f.CarrierRelationId, f.TripDate });
            b.HasIndex(f => new { f.TenantId, f.PrimaryVehicleId, f.TripDate });
            b.HasIndex(f => new { f.TenantId, f.TransportTripTypeId, f.TripDate });
            b.HasIndex(f => new { f.TenantId, f.OrganizationUnitId, f.TripDate });
        });
    }
}
