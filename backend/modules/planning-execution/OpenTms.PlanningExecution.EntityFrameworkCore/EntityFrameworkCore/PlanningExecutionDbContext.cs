using Microsoft.EntityFrameworkCore;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.EntityFrameworkCore.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.PlanningExecution.EntityFrameworkCore;

[ConnectionStringName(PlanningExecutionDbProperties.ConnectionStringName)]
public class PlanningExecutionDbContext : AbpDbContext<PlanningExecutionDbContext>
{
    /// <summary>Design 53.</summary>
    public DbSet<TransportTripType> TransportTripTypes { get; set; } = default!;

    public PlanningExecutionDbContext(DbContextOptions<PlanningExecutionDbContext> options)
        : base(options)
    {
    }

    /* Every unique key is per tenant with NULLS NOT DISTINCT and ignores soft-deleted rows (design 41's rule). */
    private const string NotDeleted = "\"IsDeleted\" = false";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("planning").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(PlanningExecutionDbProperties.DbSchema);

        builder.Entity<TransportTripType>(b =>
        {
            b.ToTable("TransportTripTypes", PlanningExecutionDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(t => t.Name).HasLocalizedNameColumn();
            b.Property(t => t.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.HasNumbering(t => t.Numbering);
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* The one default per tenant (assessment §3.8). */
            b.HasIndex(t => t.TenantId, "IX_TransportTripTypes_TenantId_Default").IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });
    }
}
