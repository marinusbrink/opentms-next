using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Published;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// Design 61 "Trip type defaults": the Master Data side of a trip type's defaults. Validates them on save (in the
/// tenant, through the published lookup: another tenant's or an unknown id is <c>ReferenceNotFound</c> with the field),
/// fills a new trip's empty groups from them, and names them for the type's detail. Every read is one batched lookup per
/// kind.
/// </summary>
public class TripTypeDefaults : ITransientDependency
{
    private readonly MasterDataReferences _masterData;

    public TripTypeDefaults(MasterDataReferences masterData)
    {
        _masterData = masterData;
    }

    /// <summary>
    /// Every default id must resolve in the tenant; a contact must be one of its carrier's and an address one of its
    /// relation's (PO 2026-10-29: a real check). The structural rules (a contact needs the carrier, an address its relation,
    /// a combination or vehicles) are the entity's.
    /// </summary>
    public async Task ValidateAsync(TransportTripTypeCreateDto input)
    {
        var relations = await _masterData.RelationsAsync([input.DefaultCarrierRelationId, input.DefaultDepartureRelationId, input.DefaultArrivalRelationId]);
        EnsureFound(relations, input.DefaultCarrierRelationId, "defaultCarrierRelationId");
        EnsureFound(relations, input.DefaultDepartureRelationId, "defaultDepartureRelationId");
        EnsureFound(relations, input.DefaultArrivalRelationId, "defaultArrivalRelationId");

        if (Given(input.DefaultCarrierContactPersonId) is { } contactId)
        {
            /* Without a carrier the entity refuses it (ContactWithoutCarrier); another relation's contact is not found here. */
            var contacts = await _masterData.ContactPersonsAsync([contactId]);
            if (!contacts.TryGetValue(contactId, out var contact) || (Given(input.DefaultCarrierRelationId) is { } carrier && contact.RelationId != carrier))
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "defaultCarrierContactPersonId").WithData("value", contactId);
            }
        }

        if (Given(input.DefaultVehicleCombinationId) is { } combinationId)
        {
            EnsureFound(await _masterData.CombinationsAsync([combinationId]), combinationId, "defaultVehicleCombinationId");
        }

        var vehicleIds = Ids(input.DefaultVehicleIds);
        if (vehicleIds.Count > 0)
        {
            var vehicles = await _masterData.VehiclesAsync(vehicleIds);
            foreach (var id in vehicleIds)
            {
                EnsureFound(vehicles, id, "defaultVehicleIds");
            }
        }

        var driverIds = Ids(input.DefaultDriverIds);
        if (driverIds.Count > 0)
        {
            var drivers = await _masterData.DriversAsync(driverIds);
            foreach (var id in driverIds)
            {
                EnsureFound(drivers, id, "defaultDriverIds");
            }
        }

        var addresses = await _masterData.AddressesAsync([input.DefaultDepartureAddressId, input.DefaultArrivalAddressId]);
        EnsureAddressOf(addresses, input.DefaultDepartureAddressId, input.DefaultDepartureRelationId, "defaultDepartureAddressId");
        EnsureAddressOf(addresses, input.DefaultArrivalAddressId, input.DefaultArrivalRelationId, "defaultArrivalAddressId");
    }

    /// <summary>
    /// A copy of the input with every group it leaves empty filled from the type (design 61 "Trip type defaults" point 2):
    /// carrier null → the type's carrier and contact (a given carrier without a contact gets the type's contact only when
    /// it is the type's carrier); neither combination nor vehicles → the type's combination or vehicles; no drivers → the
    /// type's drivers; departure relation null → the type's relation and address (a given relation without an address
    /// gets the type's address only when it is the type's relation); arrival likewise; stop composition and first/last
    /// stop rule → the type's; execution → the type's, or Subcontracted when the carrier came from the type (PO
    /// 2026-10-29). A default the lookup no longer returns, or returns archived, is skipped without an error (a contact or
    /// an address also when it no longer belongs to its carrier or relation).
    /// </summary>
    public async Task<TransportTripCreateDto> FillAsync(TransportTripCreateDto input, TransportTripType type)
    {
        var filled = Copy(input);
        filled.Execution ??= type.DefaultExecution;
        filled.StopCompositionMode ??= type.DefaultStopCompositionMode;
        filled.FirstLastStopRule ??= type.DefaultFirstLastStopRule;

        var carrierEmpty = Given(input.CarrierRelationId) == null;
        var carrierContactOnly = !carrierEmpty && Given(input.CarrierContactPersonId) == null && input.CarrierRelationId == type.DefaultCarrierRelationId;
        var departureEmpty = Given(input.DepartureRelationId) == null;
        var departureAddressOnly = !departureEmpty && Given(input.DepartureAddressId) == null && input.DepartureRelationId == type.DefaultDepartureRelationId;
        var arrivalEmpty = Given(input.ArrivalRelationId) == null;
        var arrivalAddressOnly = !arrivalEmpty && Given(input.ArrivalAddressId) == null && input.ArrivalRelationId == type.DefaultArrivalRelationId;

        /* One relation read for the three places, one address read for both addresses. */
        var relations = await _masterData.RelationsAsync([
            carrierEmpty ? type.DefaultCarrierRelationId : null,
            departureEmpty ? type.DefaultDepartureRelationId : null,
            arrivalEmpty ? type.DefaultArrivalRelationId : null]);
        var addresses = await _masterData.AddressesAsync([
            departureEmpty || departureAddressOnly ? type.DefaultDepartureAddressId : null,
            arrivalEmpty || arrivalAddressOnly ? type.DefaultArrivalAddressId : null]);

        if ((carrierEmpty && type.DefaultCarrierRelationId != null) || carrierContactOnly)
        {
            var carrierId = carrierEmpty ? Active(relations, type.DefaultCarrierRelationId) : input.CarrierRelationId;
            if (carrierId != null)
            {
                var contacts = await _masterData.ContactPersonsAsync([type.DefaultCarrierContactPersonId]);
                filled.CarrierRelationId = carrierId;
                filled.CarrierContactPersonId = ContactOf(contacts, type.DefaultCarrierContactPersonId, carrierId.Value);
                if (carrierEmpty && input.Execution == null)
                {
                    filled.Execution = TripExecution.Subcontracted;
                }
            }
        }

        if (Given(input.VehicleCombinationId) == null && Ids(input.VehicleIds).Count == 0)
        {
            if (type.DefaultVehicleCombinationId != null)
            {
                filled.VehicleCombinationId = Active(await _masterData.CombinationsAsync([type.DefaultVehicleCombinationId]), type.DefaultVehicleCombinationId);
            }
            else if (type.DefaultVehicleIds.Count > 0)
            {
                var vehicles = await _masterData.VehiclesAsync(type.DefaultVehicleIds);
                filled.VehicleIds = type.DefaultVehicleIds.Where(id => vehicles.TryGetValue(id, out var v) && !v.IsArchived).ToList();
            }
        }

        if (Ids(input.DriverIds).Count == 0 && type.DefaultDriverIds.Count > 0)
        {
            var drivers = await _masterData.DriversAsync(type.DefaultDriverIds);
            filled.DriverIds = type.DefaultDriverIds.Where(id => drivers.TryGetValue(id, out var d) && !d.IsArchived).ToList();
        }

        if (departureEmpty && Active(relations, type.DefaultDepartureRelationId) is { } departureId)
        {
            filled.DepartureRelationId = departureId;
            filled.DepartureAddressId = AddressOf(addresses, type.DefaultDepartureAddressId, departureId);
        }
        else if (departureAddressOnly)
        {
            filled.DepartureAddressId = AddressOf(addresses, type.DefaultDepartureAddressId, input.DepartureRelationId!.Value);
        }

        if (arrivalEmpty && Active(relations, type.DefaultArrivalRelationId) is { } arrivalId)
        {
            filled.ArrivalRelationId = arrivalId;
            filled.ArrivalAddressId = AddressOf(addresses, type.DefaultArrivalAddressId, arrivalId);
        }
        else if (arrivalAddressOnly)
        {
            filled.ArrivalAddressId = AddressOf(addresses, type.DefaultArrivalAddressId, input.ArrivalRelationId!.Value);
        }

        return filled;
    }

    /// <summary>Copies the type's defaults onto its detail, named; a row the lookup no longer returns is named <paramref name="notFound"/>.</summary>
    public async Task DescribeAsync(TransportTripType type, TransportTripTypeDto dto, string notFound)
    {
        dto.DefaultCarrierRelationId = type.DefaultCarrierRelationId;
        dto.DefaultCarrierContactPersonId = type.DefaultCarrierContactPersonId;
        dto.DefaultVehicleCombinationId = type.DefaultVehicleCombinationId;
        dto.DefaultVehicleIds = type.DefaultVehicleIds.ToList();
        dto.DefaultDriverIds = type.DefaultDriverIds.ToList();
        dto.DefaultDepartureRelationId = type.DefaultDepartureRelationId;
        dto.DefaultDepartureAddressId = type.DefaultDepartureAddressId;
        dto.DefaultDepartureTime = type.DefaultDepartureTime;
        dto.DefaultArrivalRelationId = type.DefaultArrivalRelationId;
        dto.DefaultArrivalAddressId = type.DefaultArrivalAddressId;
        dto.DefaultStopCompositionMode = type.DefaultStopCompositionMode;
        dto.DefaultFirstLastStopRule = type.DefaultFirstLastStopRule;

        var relations = await _masterData.RelationsAsync([type.DefaultCarrierRelationId, type.DefaultDepartureRelationId, type.DefaultArrivalRelationId]);
        dto.DefaultCarrierRelationName = NameOf(relations, type.DefaultCarrierRelationId, notFound);
        dto.DefaultDepartureRelationName = NameOf(relations, type.DefaultDepartureRelationId, notFound);
        dto.DefaultArrivalRelationName = NameOf(relations, type.DefaultArrivalRelationId, notFound);

        if (type.DefaultCarrierContactPersonId != null)
        {
            var contacts = await _masterData.ContactPersonsAsync([type.DefaultCarrierContactPersonId]);
            dto.DefaultCarrierContactPersonName = contacts.TryGetValue(type.DefaultCarrierContactPersonId.Value, out var contact) ? contact.Name : notFound;
        }

        if (type.DefaultVehicleCombinationId != null)
        {
            var combinations = await _masterData.CombinationsAsync([type.DefaultVehicleCombinationId]);
            dto.DefaultVehicleCombinationName = combinations.TryGetValue(type.DefaultVehicleCombinationId.Value, out var combination) ? combination.Name : notFound;
        }

        if (type.DefaultVehicleIds.Count > 0)
        {
            var vehicles = await _masterData.VehiclesAsync(type.DefaultVehicleIds);
            dto.DefaultVehicles = type.DefaultVehicleIds.Select(id => vehicles.TryGetValue(id, out var v)
                ? new TripTypeDefaultVehicleDto { Id = id, Name = v.Name, LicencePlate = v.LicencePlate, Kind = v.Kind, VehicleTypeName = v.VehicleTypeName }
                : new TripTypeDefaultVehicleDto { Id = id, Name = notFound }).ToList();
        }

        if (type.DefaultDriverIds.Count > 0)
        {
            var drivers = await _masterData.DriversAsync(type.DefaultDriverIds);
            dto.DefaultDrivers = type.DefaultDriverIds.Select(id => new TripTypeDefaultDriverDto { Id = id, Name = NameOf(drivers, id, notFound)! }).ToList();
        }

        var addresses = await _masterData.AddressesAsync([type.DefaultDepartureAddressId, type.DefaultArrivalAddressId]);
        dto.DefaultDepartureAddressLabel = LabelOf(addresses, type.DefaultDepartureAddressId, notFound);
        dto.DefaultArrivalAddressLabel = LabelOf(addresses, type.DefaultArrivalAddressId, notFound);
    }

    private static TransportTripCreateDto Copy(TransportTripCreateDto input) => new()
    {
        TransportTripTypeId = input.TransportTripTypeId,
        TripDate = input.TripDate,
        Name = input.Name,
        Execution = input.Execution,
        StopCompositionMode = input.StopCompositionMode,
        FirstLastStopRule = input.FirstLastStopRule,
        CarrierRelationId = input.CarrierRelationId,
        CarrierContactPersonId = input.CarrierContactPersonId,
        DepartureRelationId = input.DepartureRelationId,
        DepartureAddressId = input.DepartureAddressId,
        ArrivalRelationId = input.ArrivalRelationId,
        ArrivalAddressId = input.ArrivalAddressId,
        PlannedDeparture = input.PlannedDeparture,
        PlannedArrival = input.PlannedArrival,
        VehicleCombinationId = input.VehicleCombinationId,
        VehicleIds = input.VehicleIds.ToList(),
        DriverIds = input.DriverIds.ToList(),
        Remarks = input.Remarks,
        DriverInstructions = input.DriverInstructions,
        OrganizationUnitId = input.OrganizationUnitId,
        Attributes = new Dictionary<string, string>(input.Attributes)
    };

    private static Guid? Given(Guid? id) => id == Guid.Empty ? null : id;

    private static List<Guid> Ids(IReadOnlyList<Guid>? ids) => (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();

    private static Guid? Active(Dictionary<Guid, MasterDataReferenceDto> rows, Guid? id)
        => id != null && rows.TryGetValue(id.Value, out var row) && !row.IsArchived ? id : null;

    private static Guid? Active(Dictionary<Guid, MasterDataVehicleCombinationReferenceDto> rows, Guid? id)
        => id != null && rows.TryGetValue(id.Value, out var row) && !row.IsArchived ? id : null;

    private static Guid? ContactOf(Dictionary<Guid, MasterDataContactPersonReferenceDto> contacts, Guid? contactId, Guid carrierId)
        => contactId != null && contacts.TryGetValue(contactId.Value, out var contact) && contact.RelationId == carrierId ? contactId : null;

    private static Guid? AddressOf(Dictionary<Guid, MasterDataAddressReferenceDto> addresses, Guid? addressId, Guid relationId)
        => addressId != null && addresses.TryGetValue(addressId.Value, out var address) && address.RelationId == relationId ? addressId : null;

    private static void EnsureFound<TRow>(Dictionary<Guid, TRow> rows, Guid? id, string field)
    {
        if (Given(id) is { } key && !rows.ContainsKey(key))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field).WithData("value", key);
        }
    }

    private static void EnsureAddressOf(Dictionary<Guid, MasterDataAddressReferenceDto> addresses, Guid? addressId, Guid? relationId, string field)
    {
        if (Given(addressId) is not { } key)
        {
            return;
        }

        /* Without a relation the entity refuses it (AddressWithoutRelation); another relation's address is not found here. */
        if (Given(relationId) is { } relation && (!addresses.TryGetValue(key, out var address) || address.RelationId != relation))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field).WithData("value", key);
        }
    }

    private static string? NameOf(Dictionary<Guid, MasterDataReferenceDto> rows, Guid? id, string notFound)
        => id == null ? null : rows.TryGetValue(id.Value, out var row) ? row.Name : notFound;

    /// <summary>"Street 12, 1234 AB City", as the transport screens label an address.</summary>
    private static string? LabelOf(Dictionary<Guid, MasterDataAddressReferenceDto> addresses, Guid? id, string notFound)
    {
        if (id == null)
        {
            return null;
        }

        if (!addresses.TryGetValue(id.Value, out var a))
        {
            return notFound;
        }

        var street = string.Join(" ", new[] { a.Street, a.HouseNumber }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var place = string.Join(" ", new[] { a.PostalCode, a.City }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.Join(", ", new[] { street, place }.Where(p => p.Length > 0));
    }
}
