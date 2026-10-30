using System;
using System.Collections.Generic;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// The structural rules of a trip type's defaults (design 61 "Trip type defaults" point 1): a contact needs the carrier,
/// an address its relation, a combination or vehicles (never both), at most three vehicles and three drivers, duplicates
/// and empty ids dropped with the order kept, the composition enums checked, and a refused setter changes nothing.
/// Risk class: High (the type's validation); the Master Data side is covered by TripTypeDefaultsTests.
/// </summary>
public class TransportTripTypeDefaultsRuleTests
{
    private static TransportTripType Type()
        => new(Guid.NewGuid(), Guid.NewGuid(), "STD", LocalizedName.Create("en", "Standard"), NumberSequenceFormat.Create("TR", true, false, "-", 5));

    private static BusinessException Refused(Action action) => Should.Throw<BusinessException>(action);

    [Fact]
    public void A_new_type_has_no_defaults_and_merges_stops_without_a_first_last_rule()
    {
        var type = Type();

        type.DefaultCarrierRelationId.ShouldBeNull();
        type.DefaultCarrierContactPersonId.ShouldBeNull();
        type.DefaultVehicleCombinationId.ShouldBeNull();
        type.DefaultVehicleIds.ShouldBeEmpty();
        type.DefaultDriverIds.ShouldBeEmpty();
        type.DefaultDepartureRelationId.ShouldBeNull();
        type.DefaultDepartureAddressId.ShouldBeNull();
        type.DefaultDepartureTime.ShouldBeNull();
        type.DefaultArrivalRelationId.ShouldBeNull();
        type.DefaultArrivalAddressId.ShouldBeNull();
        type.DefaultStopCompositionMode.ShouldBe(StopCompositionMode.MergeByLocationAndActivity);
        type.DefaultFirstLastStopRule.ShouldBe(FirstLastStopRule.None);
    }

    [Fact]
    public void A_contact_needs_the_carrier_and_an_empty_id_counts_as_none()
    {
        var type = Type();
        var carrier = Guid.NewGuid();
        var contact = Guid.NewGuid();

        var error = Refused(() => type.SetDefaultCarrier(null, contact));
        error.Code.ShouldBe(PlanningExecutionErrorCodes.ContactWithoutCarrier);
        error.Data["field"].ShouldBe("defaultCarrierContactPersonId");
        Refused(() => type.SetDefaultCarrier(Guid.Empty, contact)).Code.ShouldBe(PlanningExecutionErrorCodes.ContactWithoutCarrier);

        type.SetDefaultCarrier(carrier, contact);
        (type.DefaultCarrierRelationId, type.DefaultCarrierContactPersonId).ShouldBe((carrier, contact));

        type.SetDefaultCarrier(carrier, Guid.Empty);
        (type.DefaultCarrierRelationId, type.DefaultCarrierContactPersonId).ShouldBe((carrier, (Guid?)null));

        type.SetDefaultCarrier(Guid.Empty, Guid.Empty);
        type.DefaultCarrierRelationId.ShouldBeNull();
        type.DefaultCarrierContactPersonId.ShouldBeNull();
    }

    [Fact]
    public void A_combination_and_vehicles_are_never_both_and_at_most_three_vehicles_are_kept_in_order_without_duplicates()
    {
        var type = Type();
        var (v1, v2, v3, v4) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Refused(() => type.SetDefaultVehicles(Guid.NewGuid(), [v1])).Code.ShouldBe(PlanningExecutionErrorCodes.VehicleSourceConflict);
        var tooMany = Refused(() => type.SetDefaultVehicles(null, [v1, v2, v3, v4]));
        tooMany.Code.ShouldBe(PlanningExecutionErrorCodes.TooManyVehicles);
        tooMany.Data["max"].ShouldBe(PlanningExecutionConsts.MaxVehiclesPerTrip);

        type.SetDefaultVehicles(null, [v3, v1, v3, Guid.Empty, v2, v1]);
        type.DefaultVehicleIds.ShouldBe([v3, v1, v2], "ordered, distinct, empty ids dropped; four entries naming three vehicles are fine");
        type.DefaultVehicleCombinationId.ShouldBeNull();

        var combination = Guid.NewGuid();
        type.SetDefaultVehicles(combination, [Guid.Empty]);
        type.DefaultVehicleCombinationId.ShouldBe(combination, "an empty id is no vehicle, so no conflict");
        type.DefaultVehicleIds.ShouldBeEmpty();

        type.SetDefaultVehicles(Guid.Empty, null);
        type.DefaultVehicleCombinationId.ShouldBeNull();
        type.DefaultVehicleIds.ShouldBeEmpty();
    }

    [Fact]
    public void At_most_three_drivers_are_kept_in_order_without_duplicates()
    {
        var type = Type();
        var (d1, d2, d3, d4) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var tooMany = Refused(() => type.SetDefaultDrivers([d1, d2, d3, d4]));
        tooMany.Code.ShouldBe(PlanningExecutionErrorCodes.TooManyDrivers);
        tooMany.Data["max"].ShouldBe(PlanningExecutionConsts.MaxDriversPerTrip);

        type.SetDefaultDrivers([d2, d2, d1, Guid.Empty, d3]);
        type.DefaultDriverIds.ShouldBe([d2, d1, d3]);

        type.SetDefaultDrivers(null);
        type.DefaultDriverIds.ShouldBeEmpty();
    }

    [Fact]
    public void An_address_needs_its_relation_on_departure_and_arrival()
    {
        var type = Type();
        var (relation, address) = (Guid.NewGuid(), Guid.NewGuid());

        var departure = Refused(() => type.SetDefaultDeparture(null, address, new TimeOnly(6, 0)));
        departure.Code.ShouldBe(PlanningExecutionErrorCodes.AddressWithoutRelation);
        departure.Data["field"].ShouldBe("defaultDepartureAddressId");
        var arrival = Refused(() => type.SetDefaultArrival(Guid.Empty, address));
        arrival.Code.ShouldBe(PlanningExecutionErrorCodes.AddressWithoutRelation);
        arrival.Data["field"].ShouldBe("defaultArrivalAddressId");

        type.SetDefaultDeparture(relation, address, new TimeOnly(5, 30));
        (type.DefaultDepartureRelationId, type.DefaultDepartureAddressId, type.DefaultDepartureTime).ShouldBe((relation, address, new TimeOnly(5, 30)));
        type.SetDefaultArrival(relation, null);
        (type.DefaultArrivalRelationId, type.DefaultArrivalAddressId).ShouldBe((relation, (Guid?)null), "a relation without an address is fine");

        type.SetDefaultDeparture(null, null, new TimeOnly(7, 0));
        type.DefaultDepartureRelationId.ShouldBeNull();
        type.DefaultDepartureTime.ShouldBe(new TimeOnly(7, 0), "the time is independent of the place");
    }

    [Fact]
    public void The_composition_enums_are_checked()
    {
        var type = Type();

        var mode = Refused(() => type.SetDefaultComposition((StopCompositionMode)0, FirstLastStopRule.None));
        mode.Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        mode.Data["field"].ShouldBe("defaultStopCompositionMode");
        var rule = Refused(() => type.SetDefaultComposition(StopCompositionMode.OneStopPairPerLeg, (FirstLastStopRule)4));
        rule.Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        rule.Data["field"].ShouldBe("defaultFirstLastStopRule");
        Refused(() => type.SetDefaultComposition(StopCompositionMode.OneStopPairPerLeg, (FirstLastStopRule)(-1))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);

        type.SetDefaultComposition(StopCompositionMode.OneStopPairPerLeg, FirstLastStopRule.Both);
        (type.DefaultStopCompositionMode, type.DefaultFirstLastStopRule).ShouldBe((StopCompositionMode.OneStopPairPerLeg, FirstLastStopRule.Both));
    }

    [Fact]
    public void A_refused_setter_keeps_the_previous_defaults()
    {
        var type = Type();
        var (carrier, contact, v1, d1, relation, address) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        type.SetDefaultCarrier(carrier, contact);
        type.SetDefaultVehicles(null, [v1]);
        type.SetDefaultDrivers([d1]);
        type.SetDefaultDeparture(relation, address, new TimeOnly(6, 0));
        type.SetDefaultComposition(StopCompositionMode.OneStopPairPerLeg, FirstLastStopRule.Both);

        Refused(() => type.SetDefaultCarrier(null, Guid.NewGuid()));
        Refused(() => type.SetDefaultVehicles(Guid.NewGuid(), [Guid.NewGuid()]));
        Refused(() => type.SetDefaultVehicles(null, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]));
        Refused(() => type.SetDefaultDrivers([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]));
        Refused(() => type.SetDefaultDeparture(null, Guid.NewGuid(), new TimeOnly(9, 0)));
        Refused(() => type.SetDefaultComposition(StopCompositionMode.MergeByLocationAndActivity, (FirstLastStopRule)9));

        (type.DefaultCarrierRelationId, type.DefaultCarrierContactPersonId).ShouldBe((carrier, contact));
        type.DefaultVehicleCombinationId.ShouldBeNull();
        type.DefaultVehicleIds.ShouldBe(new List<Guid> { v1 });
        type.DefaultDriverIds.ShouldBe(new List<Guid> { d1 });
        (type.DefaultDepartureRelationId, type.DefaultDepartureAddressId, type.DefaultDepartureTime).ShouldBe((relation, address, new TimeOnly(6, 0)));
        (type.DefaultStopCompositionMode, type.DefaultFirstLastStopRule).ShouldBe((StopCompositionMode.OneStopPairPerLeg, FirstLastStopRule.Both));
    }
}
