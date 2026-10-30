using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// The type of a transport trip (OpenTMS: TransportFileType; PO 2026-10-22: TransportFile is TransportTrip), design 53:
/// its number format and how a new trip of the type is executed. Exactly one default per tenant (assessment §3.8).
/// Design 61 "Trip type defaults": the values a new trip of the type gets where its input leaves them empty (Master Data
/// ids without a foreign key, validated in the tenant by the application service).
/// </summary>
public class TransportTripType : ReferenceDataAggregateRoot, IHasReferenceDataDefault
{
    public string Code { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public bool IsDefault { get; private set; }

    public NumberSequenceFormat Numbering { get; private set; } = default!;

    public TripExecution DefaultExecution { get; private set; } = TripExecution.OwnFleet;

    public bool SendToFleetManagementSystem { get; set; } = true;

    public bool IsArchived { get; private set; }

    /* Design 61 "Trip type defaults": all optional. */

    public Guid? DefaultCarrierRelationId { get; private set; }

    public Guid? DefaultCarrierContactPersonId { get; private set; }

    public Guid? DefaultVehicleCombinationId { get; private set; }

    /// <summary>Ordered and distinct, at most <see cref="PlanningExecutionConsts.MaxVehiclesPerTrip"/>; never with a combination.</summary>
    public List<Guid> DefaultVehicleIds { get; private set; } = new();

    /// <summary>Ordered and distinct, at most <see cref="PlanningExecutionConsts.MaxDriversPerTrip"/>.</summary>
    public List<Guid> DefaultDriverIds { get; private set; } = new();

    public Guid? DefaultDepartureRelationId { get; private set; }

    public Guid? DefaultDepartureAddressId { get; private set; }

    /// <summary>The local time at the departure address a new trip departs at (screens only; the server never fills it).</summary>
    public TimeOnly? DefaultDepartureTime { get; private set; }

    public Guid? DefaultArrivalRelationId { get; private set; }

    public Guid? DefaultArrivalAddressId { get; private set; }

    public StopCompositionMode DefaultStopCompositionMode { get; private set; } = StopCompositionMode.MergeByLocationAndActivity;

    public FirstLastStopRule DefaultFirstLastStopRule { get; private set; } = FirstLastStopRule.None;

    protected TransportTripType()
    {
    }

    public TransportTripType(Guid id, Guid? tenantId, string code, LocalizedName name, NumberSequenceFormat numbering)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetNumbering(numbering);
    }

    /// <summary>The key of this type's number sequence (design 50).</summary>
    public static string SequenceKeyOf(Guid id) => "PlanningExecution.TransportTripType:" + id;

    public string SequenceKey => SequenceKeyOf(Id);

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(LocalizedName name) => Name = RequireName(name);

    public void SetNumbering(NumberSequenceFormat numbering) => Numbering = Check.NotNull(numbering, nameof(numbering));

    public void SetDefaultExecution(TripExecution execution)
    {
        if (!Enum.IsDefined(execution))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "defaultExecution").WithData("value", (int)execution);
        }

        DefaultExecution = execution;
    }

    /// <summary>The carrier and its contact (design 61 "Trip type defaults"): a contact needs the carrier.</summary>
    public void SetDefaultCarrier(Guid? carrierRelationId, Guid? contactPersonId)
    {
        carrierRelationId = Empty(carrierRelationId);
        contactPersonId = Empty(contactPersonId);
        if (contactPersonId != null && carrierRelationId == null)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ContactWithoutCarrier).WithData("field", "defaultCarrierContactPersonId");
        }

        DefaultCarrierRelationId = carrierRelationId;
        DefaultCarrierContactPersonId = contactPersonId;
    }

    /// <summary>A combination or up to three vehicles, never both (as the trip's <c>SetVehicles</c>).</summary>
    public void SetDefaultVehicles(Guid? vehicleCombinationId, IReadOnlyList<Guid>? vehicleIds)
    {
        vehicleCombinationId = Empty(vehicleCombinationId);
        var ids = Distinct(vehicleIds);
        if (vehicleCombinationId != null && ids.Count > 0)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.VehicleSourceConflict);
        }

        if (ids.Count > PlanningExecutionConsts.MaxVehiclesPerTrip)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyVehicles).WithData("max", PlanningExecutionConsts.MaxVehiclesPerTrip);
        }

        DefaultVehicleCombinationId = vehicleCombinationId;
        DefaultVehicleIds = ids;
    }

    public void SetDefaultDrivers(IReadOnlyList<Guid>? driverIds)
    {
        var ids = Distinct(driverIds);
        if (ids.Count > PlanningExecutionConsts.MaxDriversPerTrip)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyDrivers).WithData("max", PlanningExecutionConsts.MaxDriversPerTrip);
        }

        DefaultDriverIds = ids;
    }

    /// <summary>The departure place and time: an address needs its relation.</summary>
    public void SetDefaultDeparture(Guid? relationId, Guid? addressId, TimeOnly? time)
    {
        (DefaultDepartureRelationId, DefaultDepartureAddressId) = Place(relationId, addressId, "defaultDepartureAddressId");
        DefaultDepartureTime = time;
    }

    /// <summary>The arrival place: an address needs its relation.</summary>
    public void SetDefaultArrival(Guid? relationId, Guid? addressId)
        => (DefaultArrivalRelationId, DefaultArrivalAddressId) = Place(relationId, addressId, "defaultArrivalAddressId");

    public void SetDefaultComposition(StopCompositionMode mode, FirstLastStopRule rule)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "defaultStopCompositionMode").WithData("value", (int)mode);
        }

        if ((int)rule < 0 || (int)rule > (int)FirstLastStopRule.Both)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "defaultFirstLastStopRule").WithData("value", (int)rule);
        }

        DefaultStopCompositionMode = mode;
        DefaultFirstLastStopRule = rule;
    }

    private static (Guid? RelationId, Guid? AddressId) Place(Guid? relationId, Guid? addressId, string addressField)
    {
        relationId = Empty(relationId);
        addressId = Empty(addressId);
        if (addressId != null && relationId == null)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.AddressWithoutRelation).WithData("field", addressField);
        }

        return (relationId, addressId);
    }

    private static Guid? Empty(Guid? id) => id == Guid.Empty ? null : id;

    private static List<Guid> Distinct(IReadOnlyList<Guid>? ids) => (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();

    public void SetArchived(bool isArchived)
    {
        if (isArchived && IsDefault)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DefaultRequired).WithData("reason", "archive");
        }

        IsArchived = isArchived;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
