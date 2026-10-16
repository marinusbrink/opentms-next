using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Fleet;

/// <summary>
/// One truck, tractor, trailer or van (design 48). The kind is its type's kind, not stored twice. Dimensions and
/// capacities are copied from the type once on create (by the service) and are the vehicle's own afterwards; the
/// characteristics are a live union of the type's and the vehicle's own. Positions and device data stay out (AO-12).
/// No personal data: default drivers are ids.
/// </summary>
public class Vehicle : MasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    /// <summary>The name in upper case: the unique key, so "Truck 01" and "truck 01" are one name (PO 2026-10-16).</summary>
    public string NormalizedName { get; private set; } = default!;

    public Guid VehicleTypeId { get; private set; }

    /// <summary>Normalised: upper case, letters and digits only. Unique per tenant and registration country when set.</summary>
    public string? LicencePlate { get; private set; }

    public Guid? RegistrationCountryId { get; private set; }

    public string? Vin { get; private set; }

    /// <summary>A relation with the Carrier or OwnCompany role (the service checks the role).</summary>
    public Guid OwnerRelationId { get; private set; }

    /// <summary>OpenTMS "Stationing": a relation with the Depot or OwnCompany role.</summary>
    public Guid? HomeDepotRelationId { get; private set; }

    public double? LengthCm { get; private set; }

    public double? WidthCm { get; private set; }

    public double? HeightCm { get; private set; }

    public double? GrossVehicleWeightKg { get; private set; }

    public double? EmptyWeightKg { get; private set; }

    public double? MaxEpq { get; private set; }

    public double? MaxLoadingMeters { get; private set; }

    public double? MaxPayloadKg { get; private set; }

    public FuelType? FuelType { get; private set; }

    public EuroClass? EuroClass { get; private set; }

    public bool IsArchived { get; private set; }

    /// <summary>The vehicle's own characteristics on top of its type's.</summary>
    public virtual ICollection<VehicleTransportCharacteristic> TransportCharacteristics { get; private set; } = new List<VehicleTransportCharacteristic>();

    public virtual ICollection<VehicleDefaultDriver> DefaultDrivers { get; private set; } = new List<VehicleDefaultDriver>();

    /// <summary>Which organisation units may use the vehicle; empty = all. Platform ids, no constraint.</summary>
    public virtual ICollection<VehicleOrganizationUnit> OrganizationUnits { get; private set; } = new List<VehicleOrganizationUnit>();

    public virtual ICollection<VehicleExternalIdentifier> ExternalIdentifiers { get; private set; } = new List<VehicleExternalIdentifier>();

    protected Vehicle()
    {
    }

    public Vehicle(Guid id, Guid? tenantId, string name, Guid vehicleTypeId, Guid ownerRelationId)
        : base(id, tenantId)
    {
        SetName(name);
        SetVehicleType(vehicleTypeId);
        SetOwnership(ownerRelationId, null);
    }

    public static string NormalizeVehicleName(string name)
        => Check.NotNullOrWhiteSpace(name, nameof(name), MasterDataConsts.MaxVehicleNameLength).Trim();

    /// <summary>The unique key of a name: trimmed and upper case.</summary>
    public static string NormalizedNameOf(string name) => NormalizeVehicleName(name).ToUpperInvariant();

    public void SetName(string name)
    {
        Name = NormalizeVehicleName(name);
        NormalizedName = Name.ToUpperInvariant();
    }

    public void SetVehicleType(Guid vehicleTypeId) => VehicleTypeId = vehicleTypeId;

    /// <summary>
    /// The plate is normalised; a plate needs its registration country; the VIN has the ISO 3779 shape. Validated
    /// before anything changes. No plate or VIN in the exception data: they identify a vehicle (rule 5).
    /// </summary>
    public void SetRegistration(string? licencePlate, Guid? registrationCountryId, string? vin)
    {
        if (!VehicleRegistration.TryNormalizeLicencePlate(licencePlate, out var plate))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidLicencePlate);
        }

        if (plate != null && registrationCountryId == null)
        {
            throw new BusinessException(MasterDataErrorCodes.RegistrationCountryRequired);
        }

        var normalizedVin = VehicleRegistration.NormalizeVin(vin);
        if (normalizedVin != null && !VehicleRegistration.IsVinShape(normalizedVin))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidVin);
        }

        LicencePlate = plate;
        RegistrationCountryId = registrationCountryId;
        Vin = normalizedVin;
    }

    /// <summary>The service checks the owner's and the home depot's roles.</summary>
    public void SetOwnership(Guid ownerRelationId, Guid? homeDepotRelationId)
    {
        if (ownerRelationId == Guid.Empty)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "ownerRelationId");
        }

        OwnerRelationId = ownerRelationId;
        HomeDepotRelationId = homeDepotRelationId;
    }

    public void SetDimensions(VehicleDimensions dimensions)
    {
        LengthCm = NonNegative(dimensions.LengthCm, "lengthCm");
        WidthCm = NonNegative(dimensions.WidthCm, "widthCm");
        HeightCm = NonNegative(dimensions.HeightCm, "heightCm");
        GrossVehicleWeightKg = NonNegative(dimensions.GrossVehicleWeightKg, "grossVehicleWeightKg");
        EmptyWeightKg = NonNegative(dimensions.EmptyWeightKg, "emptyWeightKg");
        MaxEpq = NonNegative(dimensions.MaxEpq, "maxEpq");
        MaxLoadingMeters = NonNegative(dimensions.MaxLoadingMeters, "maxLoadingMeters");
        MaxPayloadKg = NonNegative(dimensions.MaxPayloadKg, "maxPayloadKg");
    }

    public void SetEngine(FuelType? fuelType, EuroClass? euroClass)
    {
        if (fuelType is { } fuel && !Enum.IsDefined(fuel))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "fuelType").WithData("value", (int)fuel);
        }

        if (euroClass is { } euro && !Enum.IsDefined(euro))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "euroClass").WithData("value", (int)euro);
        }

        FuelType = fuelType;
        EuroClass = euroClass;
    }

    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;

    /// <summary>Replaces the vehicle's own characteristics; at most MaxTransportCharacteristicsPerVehicle distinct ones.</summary>
    public void SetTransportCharacteristics(IEnumerable<Guid> ids)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count > MasterDataConsts.MaxTransportCharacteristicsPerVehicle)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyChildren).WithData("max", MasterDataConsts.MaxTransportCharacteristicsPerVehicle).WithData("kind", "transportCharacteristics");
        }

        SyncJoin(TransportCharacteristics, wanted, c => c.TransportCharacteristicsId, id => new VehicleTransportCharacteristic(TenantId, Id, id));
    }

    /// <summary>Replaces the default drivers in the given order (SortOrder 1, 2, …); at most five, each once.</summary>
    public void SetDefaultDrivers(IReadOnlyList<Guid> orderedDriverIds)
    {
        if (orderedDriverIds.Count > MasterDataConsts.MaxDefaultDriversPerVehicle)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyChildren).WithData("max", MasterDataConsts.MaxDefaultDriversPerVehicle).WithData("kind", "defaultDrivers");
        }

        if (orderedDriverIds.Distinct().Count() != orderedDriverIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.DuplicateKey).WithData("field", "defaultDrivers").WithData("value", string.Empty);
        }

        SyncJoin(DefaultDrivers, orderedDriverIds, d => d.DriverId, id => new VehicleDefaultDriver(TenantId, Id, id));
        for (var i = 0; i < orderedDriverIds.Count; i++)
        {
            DefaultDrivers.First(d => d.DriverId == orderedDriverIds[i]).SetSortOrder(i + 1);
        }
    }

    public void SetOrganizationUnits(IEnumerable<Guid> ids) => SyncJoin(OrganizationUnits, ids, o => o.OrganizationUnitId, id => new VehicleOrganizationUnit(TenantId, Id, id));

    /// <summary>Replaces the external ids as the relation's (design 43): one (source, identifier) pair once, validated before anything changes.</summary>
    public void SetExternalIdentifiers(IEnumerable<VehicleExternalIdentifierInput> inputs, Func<Guid> newId)
    {
        var wanted = inputs.Select(i => (i.Id, Source: Text(i.Source, MasterDataConsts.MaxExternalSourceLength, "source"), Identifier: Text(i.Identifier, MasterDataConsts.MaxExternalIdentifierLength, "identifier"))).ToList();
        if (wanted.Count > MasterDataConsts.MaxExternalIdentifiersPerVehicle)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyChildren).WithData("max", MasterDataConsts.MaxExternalIdentifiersPerVehicle).WithData("kind", "externalIdentifiers");
        }

        if (wanted.Any(w => w.Source == null || w.Identifier == null))
        {
            throw new BusinessException(MasterDataErrorCodes.ExternalIdentifierIncomplete);
        }

        if (wanted.Select(w => (w.Source!.ToUpperInvariant(), w.Identifier!)).Distinct().Count() != wanted.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.DuplicateKey).WithData("field", "externalIdentifiers").WithData("value", string.Empty);
        }

        var keep = new HashSet<Guid>();
        foreach (var w in wanted)
        {
            var existing = w.Id != null ? ExternalIdentifiers.FirstOrDefault(e => e.Id == w.Id.Value) : null;
            if (existing == null)
            {
                existing = new VehicleExternalIdentifier(newId(), TenantId, Id, w.Source!, w.Identifier!);
                ExternalIdentifiers.Add(existing);
            }
            else
            {
                existing.Set(w.Source!, w.Identifier!);
            }

            keep.Add(existing.Id);
        }

        foreach (var stale in ExternalIdentifiers.Where(e => !keep.Contains(e.Id)).ToList())
        {
            ExternalIdentifiers.Remove(stale);
        }
    }

    private static double? NonNegative(double? value, string field)
    {
        if (value is { } v && (double.IsNaN(v) || double.IsInfinity(v) || v < 0))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", v);
        }

        return value;
    }

    private static void SyncJoin<TJoin>(ICollection<TJoin> rows, IEnumerable<Guid> ids, Func<TJoin, Guid> key, Func<Guid, TJoin> create)
    {
        var wanted = ids.Distinct().ToHashSet();
        foreach (var stale in rows.Where(r => !wanted.Contains(key(r))).ToList())
        {
            rows.Remove(stale);
        }

        foreach (var id in wanted.Where(id => rows.All(r => key(r) != id)))
        {
            rows.Add(create(id));
        }
    }

    private static string? Text(string? value, int maxLength, string name)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : Check.Length(text, name, maxLength);
    }
}

/// <summary>Dimensions and capacities of a vehicle; null = unknown.</summary>
public sealed record VehicleDimensions(
    double? LengthCm,
    double? WidthCm,
    double? HeightCm,
    double? GrossVehicleWeightKg,
    double? EmptyWeightKg,
    double? MaxEpq,
    double? MaxLoadingMeters,
    double? MaxPayloadKg);

public sealed record VehicleExternalIdentifierInput(Guid? Id, string Source, string Identifier);

/// <summary>A characteristic the vehicle adds to its type's; composite key, cascades from both sides.</summary>
public class VehicleTransportCharacteristic : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid VehicleId { get; private set; }

    public Guid TransportCharacteristicsId { get; private set; }

    protected VehicleTransportCharacteristic()
    {
    }

    public VehicleTransportCharacteristic(Guid? tenantId, Guid vehicleId, Guid transportCharacteristicsId)
    {
        TenantId = tenantId;
        VehicleId = vehicleId;
        TransportCharacteristicsId = transportCharacteristicsId;
    }

    public override object[] GetKeys() => [VehicleId, TransportCharacteristicsId];
}

/// <summary>A default driver of the vehicle (replaces OpenTMS's ";"-joined identifiers, assessment §3.3).</summary>
public class VehicleDefaultDriver : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid VehicleId { get; private set; }

    public Guid DriverId { get; private set; }

    /// <summary>1-based order in which the planner proposes the drivers.</summary>
    public int SortOrder { get; private set; }

    protected VehicleDefaultDriver()
    {
    }

    public VehicleDefaultDriver(Guid? tenantId, Guid vehicleId, Guid driverId)
    {
        TenantId = tenantId;
        VehicleId = vehicleId;
        DriverId = driverId;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public override object[] GetKeys() => [VehicleId, DriverId];
}

/// <summary>Which organisation units may use the vehicle; empty = all. Platform ids, no constraint.</summary>
public class VehicleOrganizationUnit : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid VehicleId { get; private set; }

    public Guid OrganizationUnitId { get; private set; }

    protected VehicleOrganizationUnit()
    {
    }

    public VehicleOrganizationUnit(Guid? tenantId, Guid vehicleId, Guid organizationUnitId)
    {
        TenantId = tenantId;
        VehicleId = vehicleId;
        OrganizationUnitId = organizationUnitId;
    }

    public override object[] GetKeys() => [VehicleId, OrganizationUnitId];
}

/// <summary>An id of the vehicle in another system (FMS, EDI, partner); unique per (source, identifier) in a tenant.</summary>
public class VehicleExternalIdentifier : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid VehicleId { get; private set; }

    public string Source { get; private set; } = default!;

    public string Identifier { get; private set; } = default!;

    protected VehicleExternalIdentifier()
    {
    }

    public VehicleExternalIdentifier(Guid id, Guid? tenantId, Guid vehicleId, string source, string identifier)
        : base(id)
    {
        TenantId = tenantId;
        VehicleId = vehicleId;
        Set(source, identifier);
    }

    public void Set(string source, string identifier)
    {
        Source = Check.NotNullOrWhiteSpace(source, nameof(source), MasterDataConsts.MaxExternalSourceLength).Trim();
        Identifier = Check.NotNullOrWhiteSpace(identifier, nameof(identifier), MasterDataConsts.MaxExternalIdentifierLength).Trim();
    }
}
