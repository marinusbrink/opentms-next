using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using OpenTms.Orders.ConsignmentNotes;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp;
using Xunit;
using static OpenTms.Orders.TransportOrders.TransportOrderBuilders;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The consignment note composer (design 58 "CMR", PO decision Q11; routing-co2-cmr.md §3.2 and scenarios 27–30): the
/// party of every box source, the goods rows and totals, the instruction and remark items, and above all that the
/// payload conforms to Platform's report data catalogue for purpose ConsignmentNote (design 56, Platform change 1).
/// Risk class: High.
/// </summary>
public class ConsignmentNoteComposerTests
{
    private static readonly Guid CarrierId = Guid.NewGuid();
    private static readonly Guid OwnId = Guid.NewGuid();
    private static readonly Guid ShipperId = Guid.NewGuid();
    private static readonly Guid TripDepartureId = Guid.NewGuid();
    private static readonly Guid TripArrivalId = Guid.NewGuid();

    private static CmrLayout Layout(
        CmrPartySource box1 = CmrPartySource.Customer, CmrPartySource box2 = CmrPartySource.DestinationAddress, CmrPartySource box3 = CmrPartySource.DestinationAddress,
        CmrPartySource box4 = CmrPartySource.OriginAddress, CmrPartySource box16 = CmrPartySource.Carrier, CmrPartySource box17 = CmrPartySource.Empty,
        CmrPartySource box21 = CmrPartySource.OwnCompany, IReadOnlyList<string>? fixedDescriptions = null)
        => new("Standaard", 4, true, true, false, box1, box2, box3, box4, box16, box17, box21,
            ConsignmentNoteProfile.OpenTmsDefaultColumns, [CmrInstructionItem.ShipperInstructions, CmrInstructionItem.FixedDescriptions, CmrInstructionItem.LineRemarks],
            [CmrRemarkItem.OrderNumber, CmrRemarkItem.CustomerReference, CmrRemarkItem.Vehicles, CmrRemarkItem.StopReferences, CmrRemarkItem.OriginReference],
            fixedDescriptions ?? ["Breekbaar"]);

    private static CmrSource Source(TransportOrder? order = null, CmrLayout? layout = null, bool withTrip = true)
    {
        order ??= Confirmed("TO-2026-00042", Line(Freight(quantity: 26, weightKg: 12480, remarks: "Gekoeld houden", payment: PaymentInstruction.Free, shipperId: ShipperId), Load(), Unload()));
        var parties = new Dictionary<Guid, CmrParty>
        {
            [CustomerId] = new(CustomerId, "Klant Foods B.V.", new CmrAddress("Dierensteinweg", "30A", "2991 XJ", "Barendrecht", "NL")),
            [DepotId] = new(DepotId, "DC Venlo Klant Foods", null),
            [ShopId] = new(ShopId, "Supermarkt Centrum Utrecht", null),
            [CarrierId] = new(CarrierId, "Charter Transport Venlo B.V.", new CmrAddress("Edisonstraat", "8", "5928 PG", "Venlo", "NL")),
            [OwnId] = new(OwnId, "Voorbeeld Transport B.V.", new CmrAddress("Waalhaven Zuidzijde", "45B", "3089 JH", "Rotterdam", "NL")),
            [ShipperId] = new(ShipperId, "Verlader B.V.", null),
            [TripDepartureId] = new(TripDepartureId, "Depot Rotterdam", null),
            [TripArrivalId] = new(TripArrivalId, "Depot Venlo", null)
        };
        var addresses = new Dictionary<Guid, CmrAddress>
        {
            [DepotAddressId] = new("Celsiusweg", "12", "5928 PR", "Venlo", "NL"),
            [ShopAddressId] = new("Vredenburg", "40", "3511 BD", "Utrecht", "NL")
        };
        var names = new CmrNames(
            new Dictionary<Guid, string> { [UnitId] = "Europallet" }, new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, string>());
        var trip = withTrip ? new CmrTripFacts(Guid.NewGuid(), "R2026-0421", new DateOnly(2026, 10, 20), "Charter Transport Venlo B.V.", ["Truck 12", "Trailer 7"], ["12-BXL-3", "OT-45-PQ"], ["Jan de Vries"]) : null;
        return new CmrSource(order, order.Lines.OrderBy(l => l.Position).ToList(), layout ?? Layout(), "CMR-2026-000123", parties[CustomerId], parties, addresses,
            parties[OwnId], parties[CarrierId], parties[ShipperId], trip, parties[TripDepartureId], parties[TripArrivalId], names,
            new DateTime(2026, 10, 19, 15, 40, 2), "m.jansen");
    }

    [Fact]
    public void The_payload_conforms_to_the_report_data_catalogue_of_purpose_ConsignmentNote()
    {
        var payload = ConsignmentNoteComposer.Compose(Source());

        payload["EntityType"]!.GetValue<string>().ShouldBe(ReportDataCatalogue.EntityTypeOf(ReportPurpose.ConsignmentNote));
        var deviations = ReportDataCatalogue.Validate(ReportPurpose.ConsignmentNote, payload);
        deviations.ShouldBeEmpty(string.Join("\n", deviations));
    }

    [Fact]
    public void The_payload_without_trip_and_without_optional_parties_still_conforms()
    {
        var layout = Layout(box1: CmrPartySource.Empty, box16: CmrPartySource.TripDeparture, box21: CmrPartySource.Shipper);
        var source = Source(layout: layout, withTrip: false) with { OwnCompany = null, Carrier = null };

        var payload = ConsignmentNoteComposer.Compose(source);

        payload["Me"].ShouldBeNull();
        payload["Entity"]!["Trip"].ShouldBeNull();
        payload["Entity"]!["Sender"].ShouldBeNull("box 1 Empty");
        ReportDataCatalogue.Validate(ReportPurpose.ConsignmentNote, payload).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CmrPartySource.Customer, "Klant Foods B.V.", "Barendrecht")]
    [InlineData(CmrPartySource.OriginAddress, "DC Venlo Klant Foods", "Venlo")]
    [InlineData(CmrPartySource.DestinationAddress, "Supermarkt Centrum Utrecht", "Utrecht")]
    [InlineData(CmrPartySource.OwnCompany, "Voorbeeld Transport B.V.", "Rotterdam")]
    [InlineData(CmrPartySource.Carrier, "Charter Transport Venlo B.V.", "Venlo")]
    [InlineData(CmrPartySource.Shipper, "Verlader B.V.", null)]
    [InlineData(CmrPartySource.TripDeparture, "Depot Rotterdam", null)]
    [InlineData(CmrPartySource.TripArrival, "Depot Venlo", null)]
    public void Every_party_source_fills_box_1_with_its_party_appendix_3_2(CmrPartySource source, string name, string? city)
    {
        var payload = ConsignmentNoteComposer.Compose(Source(layout: Layout(box1: source)));
        var sender = payload["Entity"]!["Sender"]!;

        sender["Name"]!.GetValue<string>().ShouldBe(name);
        sender["Source"]!.GetValue<string>().ShouldBe(source.ToString());
        if (city == null)
        {
            sender["Address"].ShouldBeNull();
        }
        else
        {
            sender["Address"]!["City"]!.GetValue<string>().ShouldBe(city);
        }
    }

    [Fact]
    public void Empty_leaves_the_box_empty_and_an_unknown_source_is_refused()
    {
        var payload = ConsignmentNoteComposer.Compose(Source(layout: Layout(box2: CmrPartySource.Empty)));
        payload["Entity"]!["Consignee"].ShouldBeNull();
        Should.Throw<ArgumentOutOfRangeException>(() => ConsignmentNoteComposer.Compose(Source(layout: Layout(box2: (CmrPartySource)42))));
    }

    [Fact]
    public void Boxes_3_4_and_21_carry_place_and_date()
    {
        var entity = ConsignmentNoteComposer.Compose(Source())["Entity"]!;

        var delivery = entity["DeliveryPlace"]!;
        delivery["City"]!.GetValue<string>().ShouldBe("Utrecht");
        delivery["Address"]!.GetValue<string>().ShouldBe("Vredenburg 40, 3511 BD Utrecht, NL");
        delivery["Date"]!.GetValue<string>().ShouldBe("2026-10-21", "box 3 takes the destination stop's date");

        var takingOver = entity["TakingOverPlace"]!;
        takingOver["City"]!.GetValue<string>().ShouldBe("Venlo");
        takingOver["Date"]!.GetValue<string>().ShouldBe("2026-10-20", "box 4: the first stop's date");

        var established = entity["EstablishedIn"]!;
        established["City"]!.GetValue<string>().ShouldBe("Rotterdam");
        established["Date"]!.GetValue<string>().ShouldBe("2026-10-19", "box 21: the print date");
    }

    [Fact]
    public void The_goods_rows_carry_every_column_and_the_totals_sum_them_scenario_27()
    {
        var order = Confirmed("TO-1",
            Line(Freight(quantity: 26, weightKg: 12480, remarks: "Gekoeld houden"), Load(), Unload()),
            Line(Freight(quantity: 4, weightKg: 960), Load(), Unload()));
        var entity = ConsignmentNoteComposer.Compose(Source(order))["Entity"]!;

        var goods = entity["Goods"]!.AsArray();
        goods.Count.ShouldBe(2);
        goods[0]!["Position"]!.GetValue<int>().ShouldBe(1);
        goods[0]!["Quantity"]!.GetValue<double>().ShouldBe(26);
        goods[0]!["TransportUnit"]!.GetValue<string>().ShouldBe("Europallet");
        goods[0]!["Description"]!.GetValue<string>().ShouldBe("Gekoeld houden");
        goods[0]!["OrderLineId"]!.GetValue<string>().ShouldBe(order.Lines.Single(l => l.Position == 1).Id.ToString("D"));
        entity["Totals"]!["Quantity"]!.GetValue<double>().ShouldBe(30);
        entity["Totals"]!["WeightKg"]!.GetValue<double>().ShouldBe(13440);
        entity["GoodsColumns"]!.AsArray().Select(c => c!.GetValue<string>()).ShouldContain("Totals");
        entity["ShowColumnNames"]!.GetValue<bool>().ShouldBeTrue();
        entity["GoodsDescription"]!.GetValue<string>().ShouldBe("Breekbaar", "scenario 30: fixed descriptions chosen for box 6");
        entity["OrderLineIds"]!.AsArray().Count.ShouldBe(2);
    }

    [Fact]
    public void Boxes_13_14_18_and_19_follow_the_items_of_the_layout()
    {
        var entity = ConsignmentNoteComposer.Compose(Source())["Entity"]!;

        entity["SenderInstructions"]!.GetValue<string>().ShouldBe("Breekbaar\nGekoeld houden", "shipper instructions are empty; fixed descriptions and line remarks follow in item order");
        entity["SenderInstructionItems"]!.AsArray().Select(i => i!["Item"]!.GetValue<string>()).ShouldBe(["FixedDescriptions", "LineRemarks"]);
        entity["PaymentInstruction"]!.GetValue<string>().ShouldBe("Free");
        var remarks = entity["CarrierRemarkItems"]!.AsArray().ToDictionary(i => i!["Item"]!.GetValue<string>(), i => i!["Text"]!.GetValue<string>());
        remarks["OrderNumber"].ShouldBe("TO-2026-00042");
        remarks["CustomerReference"].ShouldBe("PO-1");
        remarks["Vehicles"].ShouldBe("Truck 12 (12-BXL-3)\nTrailer 7 (OT-45-PQ)");
        remarks["StopReferences"].ShouldBe("LAAD-1\nLOS-1");
        remarks["OriginReference"].ShouldBe("LAAD-1");
        entity["CarrierRemarks"]!.GetValue<string>().ShouldStartWith("TO-2026-00042\nPO-1\n");
        entity["SpecialAgreements"].ShouldBeNull();
        entity["Carrier"]!["Name"]!.GetValue<string>().ShouldBe("Charter Transport Venlo B.V.");
        entity["SuccessiveCarrier"].ShouldBeNull("box 17 Empty by default");
        entity["Trip"]!["Number"]!.GetValue<string>().ShouldBe("R2026-0421");
        entity["Order"]!["Number"]!.GetValue<string>().ShouldBe("TO-2026-00042");
        entity["Copies"]!.GetValue<int>().ShouldBe(4);
        entity["Number"]!.GetValue<string>().ShouldBe("CMR-2026-000123");
        entity["PrintedAt"]!.GetValue<string>().ShouldBe("2026-10-19T15:40:02");
    }

    [Fact]
    public void The_signatures_name_the_sender_carrier_and_consignee_and_Me_is_the_own_company()
    {
        var payload = ConsignmentNoteComposer.Compose(Source());
        payload["Entity"]!["SenderSignature"]!["Name"]!.GetValue<string>().ShouldBe("Klant Foods B.V.");
        payload["Entity"]!["CarrierSignature"]!["Place"]!.GetValue<string>().ShouldBe("Venlo");
        payload["Entity"]!["ConsigneeSignature"]!["Name"]!.GetValue<string>().ShouldBe("Supermarkt Centrum Utrecht");
        payload["Me"]!["Name"]!.GetValue<string>().ShouldBe("Voorbeeld Transport B.V.");
        payload["Me"]!["VisitingAddress"]!["City"]!.GetValue<string>().ShouldBe("Rotterdam");
    }

    [Fact]
    public void A_print_without_lines_is_NothingToPrint()
    {
        var order = new TransportOrder(Guid.NewGuid(), TenantA, Header(), Terms(), OrderSource.Screen, Guid.NewGuid);
        Should.Throw<BusinessException>(() => ConsignmentNoteComposer.Compose(Source(order))).Code.ShouldBe(OrdersErrorCodes.NothingToPrint);
    }

    [Fact]
    public void The_profile_defaults_are_the_OpenTMS_box_defaults_and_the_layout_bounds_hold()
    {
        var profile = new ConsignmentNoteProfile(Guid.NewGuid(), TenantA, "Standaard");
        (profile.Copies, profile.ShowBorders, profile.ShowQrCode, profile.GroupByLeg).ShouldBe((4, true, true, false));
        (profile.Box1Sender, profile.Box2Consignee, profile.Box3DeliveryPlace, profile.Box4TakingOverPlace).ShouldBe((CmrPartySource.Customer, CmrPartySource.DestinationAddress, CmrPartySource.DestinationAddress, CmrPartySource.OriginAddress));
        (profile.Box16Carrier, profile.Box17SuccessiveCarrier, profile.Box21EstablishedBy).ShouldBe((CmrPartySource.Carrier, CmrPartySource.Empty, CmrPartySource.OwnCompany));
        profile.Box6Columns.ShouldBe(ConsignmentNoteProfile.OpenTmsDefaultColumns);

        Should.Throw<BusinessException>(() => profile.SetLayout(6, true, true, false)).Code.ShouldBe(OrdersErrorCodes.InvalidCopies);
        Should.Throw<BusinessException>(() => profile.SetLayout(0, true, true, false)).Code.ShouldBe(OrdersErrorCodes.InvalidCopies);
        profile.SetDefault(true);
        Should.Throw<BusinessException>(() => profile.SetArchived(true)).Code.ShouldBe(Platform.ReferenceData.ReferenceDataErrorCodes.DefaultRequired);
        profile.SetItems([CmrGoodsColumn.Quantity, CmrGoodsColumn.Quantity], [], [CmrRemarkItem.Barcode]);
        profile.Box6Columns.ShouldBe([CmrGoodsColumn.Quantity]);

        var description = new ConsignmentNoteFixedDescription(Guid.NewGuid(), TenantA, " Breekbaar ", " Voorzichtig behandelen ");
        (description.Name, description.NormalizedName, description.Text).ShouldBe(("Breekbaar", "BREEKBAAR", "Voorzichtig behandelen"));
    }
}
