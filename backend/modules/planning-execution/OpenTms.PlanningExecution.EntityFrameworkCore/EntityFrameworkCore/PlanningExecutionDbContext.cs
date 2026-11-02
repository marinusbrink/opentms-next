using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenTms.PlanningExecution.Availability;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
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

    /// <summary>Design 59.</summary>
    public DbSet<Leg> Legs { get; set; } = default!;

    public DbSet<TransportTrip> TransportTrips { get; set; } = default!;

    public DbSet<TripStop> TripStops { get; set; } = default!;

    /* Design 68: exposed so ABP registers their repositories (the Scheduler, map and resource reads query them directly). */
    public DbSet<TripVehicle> TripVehicles { get; set; } = default!;

    public DbSet<TripDriver> TripDrivers { get; set; } = default!;

    public DbSet<TripStopLeg> TripStopLegs { get; set; } = default!;

    public DbSet<ViaDepotRule> ViaDepotRules { get; set; } = default!;

    public DbSet<TripAttributeDefinition> TripAttributeDefinitions { get; set; } = default!;

    /// <summary>Design 68 §2.</summary>
    public DbSet<ResourceUnavailability> ResourceUnavailabilities { get; set; } = default!;

    public DbSet<ResourceWorkingPattern> ResourceWorkingPatterns { get; set; } = default!;

    public PlanningExecutionDbContext(DbContextOptions<PlanningExecutionDbContext> options)
        : base(options)
    {
    }

    /* Every unique key is per tenant with NULLS NOT DISTINCT and ignores soft-deleted rows (design 41's rule). */
    private const string NotDeleted = "\"IsDeleted\" = false";

    private const string Schema = PlanningExecutionDbProperties.DbSchema;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("planning").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(Schema);
        builder.HasPostgresExtension("pg_trgm");

        builder.Entity<TransportTripType>(b =>
        {
            b.ToTable("TransportTripTypes", Schema);
            b.ConfigureByConvention();
            b.Property(t => t.Code).HasMaxLength(ReferenceDataConsts.MaxCodeLength).IsRequired();
            b.Property(t => t.Name).HasLocalizedNameColumn();
            b.Property(t => t.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.HasNumbering(t => t.Numbering);
            /* Design 61 "Trip type defaults": Master Data ids without a foreign key; the id lists as jsonb arrays. */
            b.Property(t => t.DefaultVehicleIds).HasGuidListColumn();
            b.Property(t => t.DefaultDriverIds).HasGuidListColumn();
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* The one default per tenant (assessment §3.8). */
            b.HasIndex(t => t.TenantId, "IX_TransportTripTypes_TenantId_Default").IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });

        builder.Entity<Leg>(b =>
        {
            b.ToTable("Legs", Schema, t => t.HasCheckConstraint("CK_Legs_Position", "\"Position\" >= 1"));
            b.ConfigureByConvention();
            b.Ignore(l => l.Origin);
            b.Ignore(l => l.Destination);
            b.Ignore(l => l.Quantities);
            b.Property(l => l.OriginName).HasMaxLength(PlanningExecutionConsts.MaxSnapshotNameLength).IsRequired();
            b.Property(l => l.OriginCity).HasMaxLength(PlanningExecutionConsts.MaxCityLength);
            b.Property(l => l.OriginCountryCode).HasMaxLength(PlanningExecutionConsts.MaxCountryCodeLength);
            b.Property(l => l.OriginTimeZoneId).HasMaxLength(PlanningExecutionConsts.MaxTimeZoneIdLength);
            b.Property(l => l.OriginReference).HasMaxLength(PlanningExecutionConsts.MaxReferenceLength);
            b.Property(l => l.DestinationName).HasMaxLength(PlanningExecutionConsts.MaxSnapshotNameLength).IsRequired();
            b.Property(l => l.DestinationCity).HasMaxLength(PlanningExecutionConsts.MaxCityLength);
            b.Property(l => l.DestinationCountryCode).HasMaxLength(PlanningExecutionConsts.MaxCountryCodeLength);
            b.Property(l => l.DestinationTimeZoneId).HasMaxLength(PlanningExecutionConsts.MaxTimeZoneIdLength);
            b.Property(l => l.DestinationReference).HasMaxLength(PlanningExecutionConsts.MaxReferenceLength);
            b.Property(l => l.Remarks).HasMaxLength(PlanningExecutionConsts.MaxRemarksLength);
            b.Property(l => l.RouteProvider).HasMaxLength(PlanningExecutionConsts.MaxProviderLength);
            b.Property(l => l.Co2Provider).HasMaxLength(PlanningExecutionConsts.MaxProviderLength);
            b.Property(l => l.Co2FactorSet).HasMaxLength(PlanningExecutionConsts.MaxFactorSetLength);
            b.Property(l => l.EstimatedCo2Kg).HasPrecision(18, 3);
            b.Property(l => l.AllocatedCostAmount).HasPrecision(18, 2);
            b.Property(l => l.AllocatedRevenueAmount).HasPrecision(18, 2);
            b.Property(l => l.Quantity).HasPrecision(18, 4);
            b.Property(l => l.RequiredTransportCharacteristicIds).HasGuidListColumn();
            /* The match key of regeneration (design 59 "Leg"). */
            b.HasIndex(l => new { l.TenantId, l.OrderLineId, l.OriginStopId, l.DestinationStopId }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            b.HasIndex(l => new { l.TenantId, l.OrderId });
            b.HasIndex(l => new { l.TenantId, l.TripId });
            /* The unplanned-legs list. */
            b.HasIndex(l => new { l.TenantId, l.OriginPlannedDate }, "IX_Legs_TenantId_Unplanned").HasFilter("\"TripId\" IS NULL AND " + NotDeleted);
            b.HasIndex(l => new { l.TenantId, l.OriginPlannedDate });
            b.HasIndex(l => new { l.TenantId, l.CustomerRelationId });
        });

        builder.Entity<TransportTrip>(b =>
        {
            b.ToTable("TransportTrips", Schema);
            b.ConfigureByConvention();
            b.Ignore(t => t.IsFinal);
            b.Ignore(t => t.IsAssignable);
            b.Ignore(t => t.DepartureDate);
            b.Ignore(t => t.ArrivalDate);
            b.Ignore(t => t.OrderedStops);
            b.Ignore(t => t.LegIds);
            b.Ignore(t => t.VehicleIdsInOrder);
            b.Ignore(t => t.DriverIdsInOrder);
            b.Property(t => t.Number).HasMaxLength(PlanningExecutionConsts.MaxNumberLength).IsRequired();
            b.Property(t => t.Name).HasMaxLength(PlanningExecutionConsts.MaxNameLength);
            b.Property(t => t.Remarks).HasMaxLength(PlanningExecutionConsts.MaxRemarksLength);
            b.Property(t => t.DriverInstructions).HasMaxLength(PlanningExecutionConsts.MaxRemarksLength);
            b.Property(t => t.SearchKey).HasMaxLength(PlanningExecutionConsts.MaxSearchKeyLength).IsRequired();
            b.Property(t => t.LoadFactor).HasPrecision(9, 4);
            b.Property(t => t.Co2Kg).HasPrecision(18, 3);
            b.Property(t => t.CostAmount).HasPrecision(18, 2);
            b.Property(t => t.RevenueAmount).HasPrecision(18, 2);
            b.Property(t => t.Attributes)
                .HasConversion(new TripAttributesConverter(), new TripAttributesComparer())
                .HasColumnType("jsonb")
                .IsRequired();
            b.Property(t => t.Warnings)
                .HasConversion(new TripWarningsConverter(), new TripWarningsComparer())
                .HasColumnType("jsonb")
                .IsRequired();
            /* Design 68 §4: the stored route (derived values); null when routing is off. */
            b.Property(t => t.RouteSegments)
                .HasConversion(new TripRouteSegmentsConverter(), new TripRouteSegmentsComparer())
                .HasColumnType("jsonb");
            b.Property(t => t.TollAmount).HasPrecision(18, 2);
            b.Property(t => t.TollCurrencyCode).HasMaxLength(PlanningExecutionConsts.MaxCurrencyCodeLength);
            b.Property(t => t.RouteProvider).HasMaxLength(PlanningExecutionConsts.MaxProviderLength);
            b.HasOne<TransportTripType>().WithMany().HasForeignKey(t => t.TransportTripTypeId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(t => t.Vehicles).WithOne().HasForeignKey(v => v.TripId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(t => t.Drivers).WithOne().HasForeignKey(d => d.TripId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(t => t.Stops).WithOne().HasForeignKey(s => s.TripId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(t => new { t.TenantId, t.Number }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            b.HasIndex(t => new { t.TenantId, t.Status, t.TripDate });
            b.HasIndex(t => new { t.TenantId, t.CarrierRelationId });
            b.HasIndex(t => new { t.TenantId, t.PlannedDepartureUtc });
            /* Design 68 §1: the Scheduler's window overlap reads both ends. */
            b.HasIndex(t => new { t.TenantId, t.PlannedArrivalUtc });
            b.HasIndex(t => new { t.TenantId, t.CreatedFromOrderId });
            b.HasIndex(t => t.TransportTripTypeId);
            b.HasIndex(t => t.Number, "IX_TransportTrips_Number_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(t => t.Name, "IX_TransportTrips_Name_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(t => t.SearchKey, "IX_TransportTrips_SearchKey_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        builder.Entity<TripVehicle>(b =>
        {
            b.ToTable("TripVehicles", Schema, t => t.HasCheckConstraint("CK_TripVehicles_Position", $"\"Position\" >= 1 AND \"Position\" <= {PlanningExecutionConsts.MaxVehiclesPerTrip}"));
            b.ConfigureByConvention();
            b.HasKey(v => new { v.TripId, v.Position });
            b.HasIndex(v => v.VehicleId);
            /* Design 68 §1: the Scheduler's bars per vehicle row. */
            b.HasIndex(v => new { v.TenantId, v.VehicleId });
        });

        builder.Entity<TripDriver>(b =>
        {
            b.ToTable("TripDrivers", Schema, t => t.HasCheckConstraint("CK_TripDrivers_Position", $"\"Position\" >= 1 AND \"Position\" <= {PlanningExecutionConsts.MaxDriversPerTrip}"));
            b.ConfigureByConvention();
            b.HasKey(d => new { d.TripId, d.Position });
            b.HasIndex(d => d.DriverId);
            /* Design 68 §1: the Scheduler's bars per driver row. */
            b.HasIndex(d => new { d.TenantId, d.DriverId });
        });

        builder.Entity<TripStop>(b =>
        {
            b.ToTable("TripStops", Schema, t => t.HasCheckConstraint("CK_TripStops_Position", "\"Position\" >= 1"));
            b.ConfigureByConvention();
            b.Ignore(s => s.HasActuals);
            b.Ignore(s => s.HasCoordinates);
            b.Property(s => s.Name).HasMaxLength(PlanningExecutionConsts.MaxSnapshotNameLength).IsRequired();
            b.Property(s => s.City).HasMaxLength(PlanningExecutionConsts.MaxCityLength);
            b.Property(s => s.TimeZoneId).HasMaxLength(PlanningExecutionConsts.MaxTimeZoneIdLength);
            b.Property(s => s.Remarks).HasMaxLength(PlanningExecutionConsts.MaxRemarksLength);
            b.HasMany(s => s.Legs).WithOne().HasForeignKey(l => l.TripStopId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(s => new { s.TripId, s.Position });
            b.HasIndex(s => new { s.TenantId, s.RelationAddressId });
        });

        builder.Entity<TripStopLeg>(b =>
        {
            b.ToTable("TripStopLegs", Schema);
            b.ConfigureByConvention();
            b.HasKey(l => new { l.TripStopId, l.LegId, l.Role });
            /* A leg's end is at one stop (design 59 "TripStopLeg"). */
            b.HasIndex(l => new { l.LegId, l.Role }).IsUnique();
        });

        builder.Entity<ViaDepotRule>(b =>
        {
            b.ToTable("ViaDepotRules", Schema, t => t.HasCheckConstraint("CK_ViaDepotRules_Position", "\"Position\" >= 1"));
            b.ConfigureByConvention();
            b.Property(r => r.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => new { r.TenantId, r.Scope, r.Position });
            b.HasIndex(r => new { r.TenantId, r.RelationId });
            b.HasIndex(r => new { r.TenantId, r.GeographicalRegionId });
        });

        builder.Entity<ResourceUnavailability>(b =>
        {
            /* The period rule (Till after From) is the aggregate's: offsets differ per value, so no column check. */
            b.ToTable("ResourceUnavailabilities", Schema,
                t => t.HasCheckConstraint("CK_ResourceUnavailabilities_Recurrence", "(\"RecurrenceWeekdays\" IS NULL) = (\"RecurrenceUntil\" IS NULL)"));
            b.ConfigureByConvention();
            b.Ignore(u => u.IsRecurring);
            b.Property(u => u.Remarks).HasMaxLength(PlanningExecutionConsts.MaxUnavailabilityRemarksLength);
            /* Design 68 §2: the blocks of a resource's row; the blocks of every row in a window (and the calculator's check). */
            b.HasIndex(u => new { u.TenantId, u.ResourceKind, u.ResourceId, u.FromUtc });
            b.HasIndex(u => new { u.TenantId, u.FromUtc, u.SpanTillUtc });
        });

        builder.Entity<ResourceWorkingPattern>(b =>
        {
            b.ToTable("ResourceWorkingPatterns", Schema, t =>
            {
                t.HasCheckConstraint("CK_ResourceWorkingPatterns_Weekday", "\"Weekday\" >= 0 AND \"Weekday\" <= 6");
                t.HasCheckConstraint("CK_ResourceWorkingPatterns_Times", "\"End\" > \"Start\"");
            });
            b.ConfigureByConvention();
            b.HasIndex(p => new { p.TenantId, p.ResourceKind, p.ResourceId, p.Weekday }).IsUnique().AreNullsDistinct(false);
        });

        builder.Entity<TripAttributeDefinition>(b =>
        {
            b.ToTable("TripAttributeDefinitions", Schema, t => t.HasCheckConstraint("CK_TripAttributeDefinitions_Position", "\"Position\" >= 1"));
            b.ConfigureByConvention();
            b.Property(d => d.Key).HasMaxLength(PlanningExecutionConsts.MaxAttributeDefinitionKeyLength).IsRequired();
            b.Property(d => d.Name).HasLocalizedNameColumn();
            b.Property(d => d.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.HasIndex(d => new { d.TenantId, d.Key }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });
    }
}

/// <summary>The trip's attribute bag as one jsonb column (design 56 "Attributes", AD-06).</summary>
public sealed class TripAttributesConverter : ValueConverter<Dictionary<string, string>, string>
{
    public TripAttributesConverter()
        : base(a => TripAttributes.ToJson(a), json => TripAttributes.FromJson(json))
    {
    }
}

public sealed class TripAttributesComparer : ValueComparer<Dictionary<string, string>>
{
    private static bool SameAttributes(Dictionary<string, string>? a, Dictionary<string, string>? b)
        => (a == null && b == null) || (a != null && b != null && a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v == p.Value));

    public TripAttributesComparer()
        : base(
            (a, b) => SameAttributes(a, b),
            a => a.Aggregate(0, (h, p) => HashCode.Combine(h, p.Key, p.Value)),
            a => new Dictionary<string, string>(a, StringComparer.Ordinal))
    {
    }
}

/// <summary>The trip's warnings as one jsonb list of typed codes (design 59, AD-06).</summary>
public sealed class TripWarningsConverter : ValueConverter<List<TripWarning>, string>
{
    public TripWarningsConverter()
        : base(w => TripWarning.ToJson(w), json => TripWarning.FromJson(json))
    {
    }
}

public sealed class TripWarningsComparer : ValueComparer<List<TripWarning>>
{
    public TripWarningsComparer()
        : base(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            a => a.Aggregate(0, (h, w) => HashCode.Combine(h, w.GetHashCode())),
            a => a.ToList())
    {
    }
}

/// <summary>The trip's route segments as one jsonb list (design 68 §4).</summary>
public sealed class TripRouteSegmentsConverter : ValueConverter<List<TripRouteSegment>?, string?>
{
    public TripRouteSegmentsConverter()
        : base(s => s == null ? null : TripRouteSegment.ToJson(s), json => json == null ? null : TripRouteSegment.FromJson(json))
    {
    }
}

public sealed class TripRouteSegmentsComparer : ValueComparer<List<TripRouteSegment>?>
{
    public TripRouteSegmentsComparer()
        : base(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            a => a == null ? 0 : a.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            a => a == null ? null : a.ToList())
    {
    }
}

/// <summary>A list of ids as one jsonb array (the leg's required characteristics).</summary>
public static class GuidListPropertyBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<List<Guid>> HasGuidListColumn(this Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<List<Guid>> builder)
        => builder
            .HasConversion(
                new ValueConverter<List<Guid>, string>(ids => JsonSerializer.Serialize(ids, JsonOptions), json => JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? new List<Guid>()),
                new ValueComparer<List<Guid>>((a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)), a => a.Aggregate(0, (h, id) => HashCode.Combine(h, id)), a => a.ToList()))
            .HasColumnType("jsonb")
            .IsRequired();
}
