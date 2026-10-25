using Microsoft.EntityFrameworkCore;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.EntityFrameworkCore.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.Orders.EntityFrameworkCore;

[ConnectionStringName(OrdersDbProperties.ConnectionStringName)]
public class OrdersDbContext : AbpDbContext<OrdersDbContext>
{
    /* Design 51: the Orders master data. */
    public DbSet<TransportOrderType> TransportOrderTypes { get; set; } = default!;
    public DbSet<TransportOrderTypeDefaultWaypoint> TransportOrderTypeDefaultWaypoints { get; set; } = default!;
    public DbSet<WaypointType> WaypointTypes { get; set; } = default!;
    public DbSet<WaypointTypeFleetManagementCode> WaypointTypeFleetManagementCodes { get; set; } = default!;
    public DbSet<TimeSlot> TimeSlots { get; set; } = default!;

    public OrdersDbContext(DbContextOptions<OrdersDbContext> options)
        : base(options)
    {
    }

    /* Every unique key is per tenant with NULLS NOT DISTINCT and ignores soft-deleted rows (design 41's rule). */
    private const string NotDeleted = "\"IsDeleted\" = false";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("orders").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(OrdersDbProperties.DbSchema);

        builder.Entity<WaypointType>(b =>
        {
            b.ToTable("WaypointTypes", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(t => t.Name).HasLocalizedNameColumn();
            b.Property(t => t.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            /* The proposed next stop's type; restrict, so the in-use check of the delete is the truth. */
            b.HasOne<WaypointType>().WithMany().HasForeignKey(t => t.SuccessiveWaypointTypeId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(t => t.FleetManagementCodes).WithOne().HasForeignKey(c => c.WaypointTypeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* The in-use check of the waypoint type delete (another type's successor). */
            b.HasIndex(t => t.SuccessiveWaypointTypeId);
        });

        builder.Entity<WaypointTypeFleetManagementCode>(b =>
        {
            b.ToTable("WaypointTypeFleetManagementCodes", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(c => c.Vendor).HasMaxLength(OrdersConsts.MaxFleetManagementVendorLength).IsRequired();
            b.Property(c => c.Code).HasMaxLength(OrdersConsts.MaxFleetManagementCodeLength).IsRequired();
            /* One code per vendor and waypoint type. */
            b.HasIndex(c => new { c.WaypointTypeId, c.Vendor }).IsUnique();
            b.HasIndex(c => c.TenantId);
        });

        builder.Entity<TimeSlot>(b =>
        {
            b.ToTable("TimeSlots", OrdersDbProperties.DbSchema, t => t.HasCheckConstraint("CK_TimeSlots_NotEmpty", "\"StartTime\" <> \"EndTime\""));
            b.ConfigureByConvention();
            b.Property(t => t.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(t => t.Name).HasLocalizedNameColumn();
            b.Property(t => t.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.Ignore(t => t.IsOvernight);
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<TransportOrderType>(b =>
        {
            b.ToTable("TransportOrderTypes", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(t => t.Name).HasLocalizedNameColumn();
            b.Property(t => t.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.HasNumbering(t => t.Numbering);
            b.HasMany(t => t.DefaultWaypoints).WithOne().HasForeignKey(w => w.TransportOrderTypeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* The one default per tenant (assessment §3.8). */
            b.HasIndex(t => t.TenantId, "IX_TransportOrderTypes_TenantId_Default").IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });

        builder.Entity<TransportOrderTypeDefaultWaypoint>(b =>
        {
            b.ToTable("TransportOrderTypeDefaultWaypoints", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(w => new { w.TransportOrderTypeId, w.Position });
            b.HasOne<WaypointType>().WithMany().HasForeignKey(w => w.WaypointTypeId).OnDelete(DeleteBehavior.Restrict);
            /* The in-use check of the waypoint type delete (a default stop). */
            b.HasIndex(w => w.WaypointTypeId);
            b.HasIndex(w => w.TenantId);
        });
    }
}
